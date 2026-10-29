using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PhotoCapture.Host.Data;
using PhotoCapture.Host.Services;
using QRCoder;

namespace PhotoCapture.Host;

public sealed record PhotoFilter(DateOnly? From, DateOnly? To, string? Name, string? Client, string? Q);
public sealed record EditRequest(string? Name, string? Client, string? Activity, string? Note);
public sealed record RetentionRequest(bool Enabled, int Days);
public sealed record AdminPasswordRequest(string Password);

/// <summary>Endpoints for the local admin web page. Served on the admin port only (loopback, never published to the tailnet).</summary>
public static class AdminApi
{
    public static void Map(WebApplication app, SettingsStore store)
    {
        var g = app.MapGroup("/admin-api");

        g.MapGet("/photos", async (DateOnly? from, DateOnly? to, string? name, string? client, string? q, string? dateColumn, string? nameColumn, string? clientColumn, string? activityColumn, string? noteColumn, string? locationColumn, string? sort, string? direction, int? page, int? pageSize, SqlServerPhotoSource source, CancellationToken ct) =>
        {
            var allRows = await source.GetPhotosAsync(new PhotoFilter(from, to, name, client, q), ct);
            var filtered = allRows.Where(p => MatchesColumnFilters(p, dateColumn, nameColumn, clientColumn, activityColumn, noteColumn, locationColumn)).ToList();
            filtered = SortPhotos(filtered, sort ?? "date", direction ?? "desc");
            var size = Math.Clamp(pageSize ?? 25, 1, 200);
            var number = Math.Max(1, page ?? 1);
            var rows = filtered.Skip((number - 1) * size).Take(size).ToList();
            return Results.Ok(new { total = filtered.Count, page = number, pageSize = size, items = rows.Select(ToDto) });
        });


        g.MapGet("/photos/{id:guid}", async (Guid id, SqlServerPhotoSource source, CancellationToken ct) =>
        {
            var p = await source.GetPhotoAsync(id, ct);
            if (p is null) return Results.NotFound();
            return Results.Ok(new { photo = ToDto(p), history = Array.Empty<object>() });
        });

        g.MapPut("/photos/{id:guid}", async (Guid id, EditRequest body, SqlServerPhotoSource source, CancellationToken ct) =>
        {
            try
            {
                var updated = await source.UpdatePhotoAsync(id, body.Name ?? "", body.Client, body.Activity, body.Note, ct);
                return updated ? Results.Ok() : Results.NotFound();
            }
            catch (ArgumentException ex) { return Results.BadRequest(ex.Message); }
        });


        g.MapGet("/photos/{id:guid}/image", async (Guid id, SqlServerPhotoSource source, SettingsStore settings, CancellationToken ct) =>
        {
            var path = await ImagePath(settings, source, id, ct);
            return path is null ? Results.NotFound() : Results.File(path, "image/jpeg");
        });

        g.MapGet("/photos/{id:guid}/thumb", async (Guid id, SqlServerPhotoSource source, SettingsStore settings, CancellationToken ct) =>
        {
            var path = await ImagePath(settings, source, id, ct);
            if (path is null) return Results.NotFound();
            var thumb = await Thumbnails.GetAsync(settings, id, path, ct);
            return Results.File(thumb ?? path, "image/jpeg");
        });

        g.MapGet("/export.xlsx", async (DateOnly? from, DateOnly? to, string? name, string? client, string? q, string? dateColumn, string? nameColumn, string? clientColumn, string? activityColumn, string? noteColumn, string? locationColumn, string? sort, string? direction, bool? images, SqlServerPhotoSource source, SettingsStore settings, CancellationToken ct) =>
        {
            var rows = await source.GetPhotosAsync(new PhotoFilter(from, to, name, client, q), ct);
            rows = SortPhotos(rows.Where(p => MatchesColumnFilters(p, dateColumn, nameColumn, clientColumn, activityColumn, noteColumn, locationColumn)).ToList(), sort ?? "date", direction ?? "desc");
            var bytes = await ExcelExport.BuildAsync(settings, rows, images == true, ct);
            var file = $"SSD-Photos-{DateTime.Now:yyyyMMdd-HHmm}.xlsx";
            return Results.File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", file);
        });

        g.MapGet("/activities", () => Results.Ok(Activities.All));

        g.MapGet("/settings", async (SqlServerPhotoSource source, CancellationToken ct) =>
        {
            var ts = Tailscale.Status(store.Current.ApiPort, store.Current.AdminPort);
            var phoneUrl = ts.DnsName is null ? null : $"https://{ts.DnsName}";
            var remoteAdminUrl = ts.DnsName is null ? null : $"https://{ts.DnsName}:8443";
            return Results.Ok(new
            {
                dataFolder = store.DataDir,
                apiPort = store.Current.ApiPort,
                adminPort = store.Current.AdminPort,
                token = store.Current.Token,
                tokenCreatedAt = store.Current.TokenCreatedAt,
                phoneUrl,
                remoteAdminUrl,
                hasAdminPassword = store.HasAdminPassword,
                tailscale = ts,
                photoCount = await source.CountAsync(ct),
                dbSizeBytes = 0,
                version = typeof(AdminApi).Assembly.GetName().Version?.ToString(3),
            });
        });

        g.MapGet("/retention", () => Results.Conflict("Automatic cleanup is unavailable for the read-only SQL Server connection."));
        g.MapPut("/retention", () => Results.Conflict("Automatic cleanup is unavailable for the read-only SQL Server connection."));

        g.MapPost("/settings/regenerate-token", () => Results.Ok(new { token = store.Regenerate() }));

        g.MapPost("/settings/tailscale-serve", () =>
        {
            var (ok, message) = Tailscale.EnableServe(store.Current.ApiPort);
            return Results.Ok(new { ok, message });
        });

        g.MapPost("/settings/admin-password", (AdminPasswordRequest body) =>
        {
            if (string.IsNullOrWhiteSpace(body.Password) || body.Password.Length < 8)
                return Results.BadRequest("Password must be at least 8 characters.");
            store.SetAdminPassword(body.Password);
            return Results.Ok();
        });

        g.MapPost("/settings/tailscale-serve-admin", () =>
        {
            if (!store.HasAdminPassword) return Results.Conflict("Set an admin password first.");
            var (ok, message) = Tailscale.EnableAdminServe(store.Current.AdminPort);
            return Results.Ok(new { ok, message });
        });

        // Setup QR: {"u": phone URL, "t": token}. Admin port only, so the token never leaves this PC.
        g.MapGet("/setup-qr.svg", (string? url) =>
        {
            var ts = Tailscale.Status(store.Current.ApiPort, store.Current.AdminPort);
            var phoneUrl = !string.IsNullOrWhiteSpace(url) ? url.Trim() : ts.DnsName is null ? null : $"https://{ts.DnsName}";
            if (phoneUrl is null) return Results.NotFound();
            var payload = JsonSerializer.Serialize(new { u = phoneUrl.TrimEnd('/'), t = store.Current.Token });
            using var generator = new QRCodeGenerator();
            using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
            var svg = new SvgQRCode(data).GetGraphic(6, "#00008b", "#ffffff", drawQuietZones: true);
            return Results.Content(svg, "image/svg+xml");
        });
    }

    static IQueryable<Photo> Filter(IQueryable<Photo> q, PhotoFilter f)
    {
        if (f.From.HasValue) q = q.Where(p => p.CaptureDate >= f.From.Value);
        if (f.To.HasValue) q = q.Where(p => p.CaptureDate <= f.To.Value);
        if (!string.IsNullOrWhiteSpace(f.Name)) { var v = f.Name.Trim(); q = q.Where(p => p.Name.Contains(v)); }
        if (!string.IsNullOrWhiteSpace(f.Client)) { var v = f.Client.Trim(); q = q.Where(p => p.Client != null && p.Client.Contains(v)); }
        if (!string.IsNullOrWhiteSpace(f.Q))
        {
            var v = f.Q.Trim();
            q = q.Where(p => p.Name.Contains(v) || (p.Client != null && p.Client.Contains(v)) || (p.Note != null && p.Note.Contains(v)) || (p.Address != null && p.Address.Contains(v)));
        }
        return q;
    }

    static bool MatchesColumnFilters(Photo p, string? date, string? name, string? client, string? activity, string? note, string? location)
    {
        static bool Has(string? value, string? query) => string.IsNullOrWhiteSpace(query) || (value ?? "").Contains(query.Trim(), StringComparison.OrdinalIgnoreCase);
        return (string.IsNullOrWhiteSpace(date) || p.CaptureDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) == date.Trim())
            && Has(p.Name, name) && Has(p.Client, client) && Has(p.Activity, activity) && Has(p.Note, note)
            && Has($"{p.Address} {p.Latitude} {p.Longitude}", location);
    }

    static List<Photo> SortPhotos(List<Photo> rows, string sort, string direction)
    {
        var ordered = sort.ToLowerInvariant() switch
        {
            "name" => direction == "asc" ? rows.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase) : rows.OrderByDescending(p => p.Name, StringComparer.OrdinalIgnoreCase),
            "client" => direction == "asc" ? rows.OrderBy(p => p.Client, StringComparer.OrdinalIgnoreCase) : rows.OrderByDescending(p => p.Client, StringComparer.OrdinalIgnoreCase),
            "activity" => direction == "asc" ? rows.OrderBy(p => p.Activity, StringComparer.OrdinalIgnoreCase) : rows.OrderByDescending(p => p.Activity, StringComparer.OrdinalIgnoreCase),
            "note" => direction == "asc" ? rows.OrderBy(p => p.Note, StringComparer.OrdinalIgnoreCase) : rows.OrderByDescending(p => p.Note, StringComparer.OrdinalIgnoreCase),
            "location" => direction == "asc" ? rows.OrderBy(p => p.Address, StringComparer.OrdinalIgnoreCase) : rows.OrderByDescending(p => p.Address, StringComparer.OrdinalIgnoreCase),
            _ => direction == "asc" ? rows.OrderBy(p => p.CaptureDate).ThenBy(p => p.CaptureTime) : rows.OrderByDescending(p => p.CaptureDate).ThenByDescending(p => p.CaptureTime),
        };
        return ordered.ToList();
    }

    static object ToDto(Photo p) => new
    {
        p.Id, p.Name, p.Client, p.Activity, p.Note, p.Address, p.Latitude, p.Longitude, p.Accuracy,
        capturedAt = p.CapturedAt,
        captureDate = p.CaptureDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        captureTime = p.CaptureTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
        receivedAt = p.ReceivedAt,
        updatedAt = p.UpdatedAt,
        mapsUrl = ExcelExport.MapsUrl(p),
        thumbUrl = $"/admin-api/photos/{p.Id}/thumb",
        imageUrl = $"/admin-api/photos/{p.Id}/image",
    };

    static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    public static async Task<string?> ImagePath(SettingsStore store, SqlServerPhotoSource source, Guid id, CancellationToken ct)
    {
        var photo = await source.GetPhotoAsync(id, ct);
        if (photo is null) return null;
        var photosRoot = Path.GetFullPath(store.PhotosDir);
        var full = Path.GetFullPath(Path.Combine(photosRoot, photo.ImagePath));
        if (!full.StartsWith(photosRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(full)) return null;
        return full;
    }
}
