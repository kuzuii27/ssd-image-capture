using System.Diagnostics;
using System.Net;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PhotoCapture.Host.Data;
using PhotoCapture.Host.Services;

namespace PhotoCapture.Host;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        using var singleInstance = new Mutex(true, @"Local\SSDPhotoCapture.Host", out var isFirst);

        var dataDir = DataFolder.Resolve(args);
        SettingsStore store;
        try { store = new SettingsStore(dataDir); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show($"The data folder cannot be used:\n{dataDir}\n\n{ex.Message}", "SSD Photo Capture", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        if (!isFirst)
        {
            // Clicking the icon again just brings up the admin page of the running copy.
            OpenAdmin(store.Current.AdminPort);
            return;
        }

        WebApplication app;
        try
        {
            app = BuildWebApp(store);
            using (var scope = app.Services.CreateScope())
                Schema.Apply(scope.ServiceProvider.GetRequiredService<PhotosDb>());
            app.StartAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            store.Log($"Startup failed: {ex}");
            var hint = ex is IOException or System.Net.Sockets.SocketException || ex.InnerException is System.Net.Sockets.SocketException
                ? $"\n\nPorts {store.Current.ApiPort} and {store.Current.AdminPort} must be free. Another program may be using them."
                : "";
            MessageBox.Show($"SSD Photo Capture could not start.\n\n{ex.Message}{hint}\n\nLog: {store.LogPath}", "SSD Photo Capture", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var restart = false;
        using (var tray = new TrayApp(store, () => OpenAdmin(store.Current.AdminPort), () => restart = true))
        {
            if (!args.Contains("--background")) OpenAdmin(store.Current.AdminPort);
            else tray.Notify("SSD Photo Capture is running", "Right-click the tray icon to open the admin page.");
            Application.Run();
        }

        try { app.StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult(); } catch { }
        if (restart)
        {
            singleInstance.ReleaseMutex();
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true });
        }
    }

    internal static void OpenAdmin(int port)
    {
        try { Process.Start(new ProcessStartInfo($"http://localhost:{port}/") { UseShellExecute = true }); }
        catch { /* no default browser; nothing sensible to do */ }
    }

    private static WebApplication BuildWebApp(SettingsStore store)
    {
        var s = store.Current;
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = AppContext.BaseDirectory,
            WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot"),
        });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(k =>
        {
            k.AddServerHeader = false;
            k.Limits.MaxRequestBodySize = s.MaxUploadBytes + 1024 * 1024;
            // Loopback only: phones reach the API through `tailscale serve`, never directly.
            foreach (var port in new[] { s.ApiPort, s.AdminPort })
            {
                k.Listen(IPAddress.Loopback, port);
                k.Listen(IPAddress.IPv6Loopback, port);
            }
        });
        builder.Services.AddSingleton(store);
        builder.Services.AddSingleton<RetentionService>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<RetentionService>());
        builder.Services.AddDbContext<PhotosDb>(o => o.UseSqlite($"Data Source={store.DbPath}"));
        EnsureSqlServerDatabase(store.SqlServerMdfPath);
        var connectionString = $"Server=(localdb)\\MSSQLLocalDB;AttachDbFilename={store.SqlServerMdfPath};Database=SSD_mobileCamera;Integrated Security=true;TrustServerCertificate=true;Connect Timeout=5";
        EnsureActivityColumns(connectionString);
        var sqlServerPhotos = new SqlServerPhotoSource(connectionString);
        builder.Services.AddSingleton(sqlServerPhotos);
        builder.Services.AddSingleton(new SettingsStorePhotoStorage(store));
        builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase);

        var app = builder.Build();
        app.Use(async (ctx, next) =>
        {
            var port = ctx.Connection.LocalPort;
            var isApi = ctx.Request.Path.StartsWithSegments("/api");

            if (port == s.ApiPort)
            {
                // Phone port: nothing but the mobile API, and only with the token (health is public so "test connection" can explain a bad token).
                if (!isApi) { ctx.Response.StatusCode = 404; return; }
                if (!ctx.Request.Path.StartsWithSegments("/api/health") && !store.IsValidToken(MobileApi.BearerOf(ctx)))
                {
                    ctx.Response.StatusCode = 401;
                    return;
                }
            }
            else if (port == s.AdminPort)
            {
                if (isApi) { ctx.Response.StatusCode = 404; return; }
                var host = ctx.Request.Host.Host;
                var isLocal = host is "localhost" or "127.0.0.1" or "[::1]";
                if (!isLocal)
                {
                    // Remote (Tailscale) access: only if a password is set, and only with it supplied via Basic Auth.
                    if (!store.HasAdminPassword) { ctx.Response.StatusCode = 403; return; }
                    var header = ctx.Request.Headers.Authorization.ToString();
                    var ok = false;
                    if (header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header[6..]));
                            var sep = decoded.IndexOf(':');
                            ok = store.IsValidAdminPassword(sep >= 0 ? decoded[(sep + 1)..] : decoded);
                        }
                        catch (FormatException) { }
                    }
                    if (!ok)
                    {
                        ctx.Response.StatusCode = 401;
                        ctx.Response.Headers.WWWAuthenticate = "Basic realm=\"SSD Photo Capture\"";
                        return;
                    }
                }
                if (!HttpMethods.IsGet(ctx.Request.Method) && !HttpMethods.IsHead(ctx.Request.Method))
                {
                    // CSRF defense: browsers cannot send this custom header cross-origin without a CORS preflight, which we never approve.
                    var origin = ctx.Request.Headers.Origin.ToString();
                    var originOk = origin.Length == 0 || origin.EndsWith(ctx.Request.Host.Value, StringComparison.OrdinalIgnoreCase);
                    if (!originOk || ctx.Request.Headers["X-Admin-Request"] != "1") { ctx.Response.StatusCode = 403; return; }
                }
                ctx.Response.Headers.ContentSecurityPolicy = "default-src 'self'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; frame-ancestors 'none'";
                ctx.Response.Headers.XContentTypeOptions = "nosniff";
                ctx.Response.Headers.CacheControl = "no-store";
            }
            else { ctx.Response.StatusCode = 404; return; }
            await next();
        });

        app.UseExceptionHandler(e => e.Run(async ctx =>
        {
            var ex = ctx.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
            if (ex is not null) store.Log($"{ctx.Request.Method} {ctx.Request.Path}: {ex}");
            ctx.Response.StatusCode = 500;
            await ctx.Response.WriteAsync("Unexpected error. See the log in the data folder.");
        }));

        app.UseDefaultFiles();
        app.UseStaticFiles();
        MobileApi.Map(app, store);
        AdminApi.Map(app, store);
        return app;
    }

    // First run on a PC, or data folder copied without the SQL files: create the LocalDB database from schema.sql.
    static void EnsureActivityColumns(string connectionString)
    {
        // shortcut: idempotent column-add migration, not a full migration framework — upgrade if more columns get added later.
        using var connection = new SqlConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = @"
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Photos') AND name = 'Activity')
    ALTER TABLE dbo.Photos ADD Activity nvarchar(50) NULL;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Photos') AND name = 'UpdatedAt')
    ALTER TABLE dbo.Photos ADD UpdatedAt datetimeoffset NULL;";
        command.ExecuteNonQuery();
    }

    static void EnsureSqlServerDatabase(string mdfPath)
    {
        if (File.Exists(mdfPath)) return;
        var ldfPath = Path.Combine(Path.GetDirectoryName(mdfPath)!, Path.GetFileNameWithoutExtension(mdfPath) + "_log.ldf");
        using var connection = new SqlConnection("Server=(localdb)\\MSSQLLocalDB;Integrated Security=true;TrustServerCertificate=true;Connect Timeout=5");
        connection.Open();
        using (var create = connection.CreateCommand())
        {
            create.CommandText = $"CREATE DATABASE SSD_mobileCamera ON (NAME = SSD_mobileCamera, FILENAME = '{mdfPath}') LOG ON (NAME = SSD_mobileCamera_log, FILENAME = '{ldfPath}')";
            create.ExecuteNonQuery();
        }
        using (var schema = connection.CreateCommand())
        {
            schema.CommandText = @"USE SSD_mobileCamera;
CREATE TABLE Photos (
    Id uniqueidentifier NOT NULL PRIMARY KEY,
    Name nvarchar(200) NOT NULL,
    Latitude float NOT NULL,
    Longitude float NOT NULL,
    Accuracy float NOT NULL,
    CapturedAt datetimeoffset NOT NULL,
    CaptureDate date NOT NULL,
    CaptureTime time NOT NULL,
    ReceivedAt datetimeoffset NOT NULL,
    ImagePath nvarchar(500) NOT NULL,
    Client nvarchar(200) NULL,
    Note nvarchar(2000) NULL,
    Address nvarchar(500) NULL,
    Activity nvarchar(50) NULL,
    UpdatedAt datetimeoffset NULL
);
CREATE INDEX IX_Photos_CaptureDate ON Photos(CaptureDate);";
            schema.ExecuteNonQuery();
        }
        using (var detach = connection.CreateCommand())
        {
            detach.CommandText = "ALTER DATABASE SSD_mobileCamera SET SINGLE_USER WITH ROLLBACK IMMEDIATE; EXEC sp_detach_db 'SSD_mobileCamera'";
            detach.ExecuteNonQuery();
        }
    }
}
