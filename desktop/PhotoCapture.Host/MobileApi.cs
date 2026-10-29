using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PhotoCapture.Host.Data;
using PhotoCapture.Host.Services;

namespace PhotoCapture.Host;

/// <summary>Endpoints the phone app calls. Served on the API port only, always behind the bearer token.</summary>
public static class MobileApi
{
    public static void Map(WebApplication app, SettingsStore store)
    {
        var storageRoot = store.PhotosDir;
        var maxBytes = store.Current.MaxUploadBytes;
        var sqlPhotos = app.Services.GetRequiredService<SqlServerPhotoSource>();

        app.MapGet("/api/health", (HttpContext ctx) =>
            Results.Ok(new { status = "ok", authorized = store.IsValidToken(BearerOf(ctx)) }));

        app.MapPost("/api/photos", async ([FromForm] UploadPhoto form, CancellationToken ct) =>
        {
            try
            {
                var saved = await sqlPhotos.SavePhoneUploadAsync(form, storageRoot, maxBytes, ct);
                return Results.Ok(new { id = form.Id, status = saved ? "saved" : "already-saved" });
            }
            catch (ArgumentException ex) { return Results.BadRequest(ex.Message); }
        }).DisableAntiforgery();
    }

    public static string? BearerOf(HttpContext ctx)
    {
        var header = ctx.Request.Headers.Authorization.ToString();
        return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header[7..].Trim() : null;
    }
}

public sealed class UploadPhoto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Client { get; set; }
    public string? Activity { get; set; }
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
