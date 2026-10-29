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
        var sqlTasks = app.Services.GetRequiredService<SqlServerTaskSource>();

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

        app.MapPut("/api/tasks/{id:guid}", async (Guid id, TaskUpsert body, CancellationToken ct) =>
        {
            if (!string.IsNullOrWhiteSpace(body.Id) && !(Guid.TryParse(body.Id, out var bodyId) && bodyId == id))
                return Results.BadRequest("The id in the body must match the id in the URL.");
            try
            {
                if (await sqlTasks.UpsertTaskAsync(body with { Id = id.ToString() }, ct)) return Results.Ok(new { id, status = "saved" });
                // Only a stored row can be ignored, so its review status tells the phone whether to stop (approved) or just resync later.
                var review = (await sqlTasks.GetTaskAsync(id, ct))?.ReviewStatus;
                return Results.Ok(new { id, status = review == "approved" ? "locked" : "ignored-stale", reviewStatus = review });
            }
            catch (ArgumentException ex) { return Results.BadRequest(ex.Message); }
        });

        app.MapPost("/api/tasks/{id:guid}/photos", async (Guid id, [FromForm] UploadTaskPhoto form, CancellationToken ct) =>
        {
            try
            {
                var saved = await sqlTasks.SaveTaskPhotoAsync(form.Id, id, form.Kind, form.captured_at, form.Image, storageRoot, maxBytes, ct);
                return Results.Ok(new { id = form.Id, status = saved ? "saved" : "already-saved" });
            }
            catch (ArgumentException ex) { return Results.BadRequest(ex.Message); }
        }).DisableAntiforgery();

        app.MapPost("/api/tasks/{id:guid}/resubmit", async (Guid id, CancellationToken ct) =>
        {
            if (await sqlTasks.ResubmitTaskAsync(id, ct)) return Results.Ok(new { id, status = "pending" });
            var task = await sqlTasks.GetTaskAsync(id, ct);
            return task is null
                ? Results.NotFound("Unknown task id.")
                : Results.Conflict($"Only a rejected task can be resubmitted; this one is {task.ReviewStatus}.");
        });

        app.MapPost("/api/tasks/review-states", async (ReviewStatesRequest body, CancellationToken ct) =>
        {
            var raw = body.Ids ?? [];
            if (raw.Count > 500) return Results.BadRequest("At most 500 ids per request.");
            // A corrupt local row must not block the whole sync, so unparseable ids are skipped.
            var ids = raw.Select(x => Guid.TryParse(x, out var g) ? g : Guid.Empty).Where(g => g != Guid.Empty).ToList();
            return Results.Ok(await sqlTasks.GetReviewStatesAsync(ids, ct));
        });
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

/// <summary>One task photo. Id is the PHOTO's own uuid; the task id comes from the route.</summary>
public sealed class UploadTaskPhoto
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "";
    public string captured_at { get; set; } = "";
    public IFormFile Image { get; set; } = null!;
}

public sealed record ReviewStatesRequest(List<string>? Ids);
