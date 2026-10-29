using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("Photos");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("Set ConnectionStrings:Photos using user-secrets or environment variables.");
var apiKey = builder.Configuration["ApiKey"];
if (string.IsNullOrWhiteSpace(apiKey))
    throw new InvalidOperationException("Set ApiKey using user-secrets or environment variables.");
var storageRoot = Path.GetFullPath(builder.Configuration["PhotoStorage:Root"] ?? "photos");
var maxBytes = builder.Configuration.GetValue<long>("PhotoStorage:MaxUploadBytes", 15 * 1024 * 1024);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddDbContext<PhotosDb>(options => options.UseSqlServer(connectionString));
var app = builder.Build();


app.UseHttpsRedirection();
app.Use(async (context, next) =>
{
    var supplied = context.Request.Headers.Authorization.ToString();
    var apiKeyBytes = System.Text.Encoding.UTF8.GetBytes(apiKey);
    if (!supplied.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ||
        System.Text.Encoding.UTF8.GetByteCount(supplied[7..]) != apiKeyBytes.Length ||
        !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(supplied[7..]), apiKeyBytes))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return;
    }
    await next();
});

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/api/photos", async ([FromForm] UploadPhoto form, PhotosDb db, CancellationToken ct) =>
{
    if (!Guid.TryParse(form.Id, out var id) || id == Guid.Empty) return Results.BadRequest("id must be a non-empty UUID.");
    if (string.IsNullOrWhiteSpace(form.Name) || form.Name.Trim().Length > 200) return Results.BadRequest("name is required (max 200 characters).");
    if (await db.Photos.AnyAsync(p => p.Id == id, ct)) return Results.Ok(new { id, status = "already-saved" });
    if (form.Image is null || form.Image.Length == 0 || form.Image.Length > maxBytes) return Results.BadRequest($"image must be 1..{maxBytes} bytes.");
    if (!string.Equals(form.Image.ContentType, "image/jpeg", StringComparison.OrdinalIgnoreCase)) return Results.BadRequest("Only JPEG images are accepted.");
    if (!double.IsFinite(form.Latitude) || form.Latitude is < -90 or > 90 || !double.IsFinite(form.Longitude) || form.Longitude is < -180 or > 180 || !double.IsFinite(form.Accuracy) || form.Accuracy < 0)
        return Results.BadRequest("Invalid latitude, longitude, or accuracy.");
    if (!DateTimeOffset.TryParse(form.captured_at, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var capturedAt) || capturedAt.Offset != TimeSpan.FromHours(8))
        return Results.BadRequest("captured_at must be an ISO-8601 timestamp with +08:00 offset.");
    if (!DateOnly.TryParseExact(form.capture_date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) || date != DateOnly.FromDateTime(capturedAt.DateTime))
        return Results.BadRequest("capture_date must match captured_at in +08:00.");
    if (!TimeOnly.TryParseExact(form.capture_time, "HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) || time != TimeOnly.FromDateTime(capturedAt.DateTime))
        return Results.BadRequest("capture_time must match captured_at in +08:00.");

    var header = new byte[3];
    await using (var stream = form.Image.OpenReadStream())
    {
        if (await stream.ReadAsync(header, ct) != header.Length || header[0] != 0xFF || header[1] != 0xD8 || header[2] != 0xFF)
            return Results.BadRequest("File is not a JPEG image.");
    }

    var folder = Path.Combine(storageRoot, date.Year.ToString("0000"), date.Month.ToString("00"), date.Day.ToString("00"));
    Directory.CreateDirectory(folder);
    var path = Path.Combine(folder, $"{id:N}.jpg");
    var relativePath = Path.GetRelativePath(storageRoot, path);
    try
    {
        await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            await form.Image.CopyToAsync(output, ct);
        db.Photos.Add(new Photo { Id = id, Name = form.Name.Trim(), Client = form.Client?.Trim(), Note = form.Note?.Trim(), Address = form.Address?.Trim(), Latitude = form.Latitude, Longitude = form.Longitude, Accuracy = form.Accuracy, CapturedAt = capturedAt, CaptureDate = date, CaptureTime = time, ReceivedAt = DateTimeOffset.UtcNow, ImagePath = relativePath });
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/photos/{id}", new { id, status = "saved" });
    }
    catch (DbUpdateException)
    {
        if (await db.Photos.AnyAsync(p => p.Id == id, ct)) return Results.Ok(new { id, status = "already-saved" });
        TryDelete(path);
        return Results.Problem("Unable to save photo record; retry with the same id.", statusCode: 503);
    }
    catch
    {
        TryDelete(path);
        throw;
    }
}).DisableAntiforgery();

app.MapGet("/api/photos/{id:guid}/image", async (Guid id, PhotosDb db, CancellationToken ct) =>
{
    var photo = await db.Photos.FindAsync([id], ct);
    if (photo is null) return Results.NotFound();
    var path = Path.GetFullPath(Path.Combine(storageRoot, photo.ImagePath));
    if (!path.StartsWith(storageRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) return Results.NotFound();
    return Results.File(path, "image/jpeg");
});

app.MapGet("/api/photos", async (DateOnly? date, string? name, string? client, PhotosDb db, CancellationToken ct) =>
{
    var query = db.Photos.AsNoTracking().AsQueryable();
    if (date.HasValue) query = query.Where(p => p.CaptureDate == date.Value);
    if (!string.IsNullOrWhiteSpace(name)) query = query.Where(p => p.Name.Contains(name));
    if (!string.IsNullOrWhiteSpace(client)) query = query.Where(p => p.Client != null && p.Client.Contains(client));
    var rows = await query.OrderByDescending(p => p.CapturedAt).Take(500).Select(p => new
    {
        p.Id, p.Name, p.Client, p.Note, p.Address, p.Latitude, p.Longitude, p.Accuracy, p.CapturedAt, p.CaptureDate, p.CaptureTime, p.ReceivedAt,
        imageUrl = $"/api/photos/{p.Id}/image"
    }).ToListAsync(ct);
    return Results.Ok(rows);
});

app.Run();

static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }

public sealed class UploadPhoto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Client { get; set; }
    public string? Note { get; set; }
    public string? Address { get; set; }

    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double Accuracy { get; set; }
    public string captured_at { get; set; } = "";
    public string capture_date { get; set; } = "";
    public string capture_time { get; set; } = "";
    public IFormFile Image { get; set; } = null!;
}

public sealed class Photo
{
    public Guid Id { get; set; }
    [MaxLength(200)] public string Name { get; set; } = "";
    [MaxLength(200)] public string? Client { get; set; }
    [MaxLength(2000)] public string? Note { get; set; }
    [MaxLength(500)] public string? Address { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double Accuracy { get; set; }
    public DateTimeOffset CapturedAt { get; set; }
    public DateOnly CaptureDate { get; set; }
    public TimeOnly CaptureTime { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    [MaxLength(500)] public string ImagePath { get; set; } = "";
}

public sealed class BearerToken
{
    public int Id { get; set; }
    public byte[] TokenHash { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

public sealed class PhotosDb(DbContextOptions<PhotosDb> options) : DbContext(options)
{
    public DbSet<Photo> Photos => Set<Photo>();
    public DbSet<BearerToken> BearerTokens => Set<BearerToken>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var photo = modelBuilder.Entity<Photo>();
        photo.ToTable("Photos");
        photo.HasKey(p => p.Id);
        photo.HasIndex(p => p.CaptureDate);
        photo.Property(p => p.CaptureDate).HasColumnType("date");
        photo.Property(p => p.CaptureTime).HasColumnType("time");
        photo.Property(p => p.CapturedAt).HasColumnType("datetimeoffset");
        photo.Property(p => p.ReceivedAt).HasColumnType("datetimeoffset");
        photo.Property(p => p.Name).HasMaxLength(200).IsRequired();
        photo.Property(p => p.ImagePath).HasMaxLength(500).IsRequired();
        photo.Property(p => p.Client).HasMaxLength(200);
        photo.Property(p => p.Note).HasMaxLength(2000);
        var token = modelBuilder.Entity<BearerToken>();
        token.ToTable("BearerTokens");
        token.HasIndex(t => t.TokenHash).IsUnique();
        token.Property(t => t.TokenHash).HasColumnType("binary(32)").HasMaxLength(32).IsRequired();
    }
}
