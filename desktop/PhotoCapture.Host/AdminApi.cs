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
public sealed record TaskEditRequest(string? Client, string? JobType, string? OldImei, string? NewImei, string? OldSim, string? NewSim, string? PlateNumber, string? Note);
public sealed record RejectRequest(string? Reason);

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

        // ---------- task lifecycle (time in -> client -> job -> time out -> admin review) ----------

        g.MapGet("/tasks", async (DateOnly? from, DateOnly? to, string? name, string? client, string? status, string? reviewStatus, string? jobType, string? q, string? sort, string? direction, int? page, int? pageSize, SqlServerTaskSource tasks, CancellationToken ct) =>
        {
            // "awaiting" is a UI concept: a completed task whose review status is still none or pending.
            var awaiting = string.Equals(reviewStatus, "awaiting", StringComparison.OrdinalIgnoreCase);
            var rows = await tasks.GetTasksAsync(new TaskFilter(from, to, name, client, awaiting ? "complete" : status, awaiting ? null : reviewStatus, jobType, q), ct);
            if (awaiting) rows = rows.Where(IsAwaitingReview).ToList();
            rows = SortTasks(rows, sort ?? "timeIn", direction ?? "desc");
            var size = Math.Clamp(pageSize ?? 25, 1, 200);
            var number = Math.Max(1, page ?? 1);
            var paged = rows.Skip((number - 1) * size).Take(size).ToList();
            var photoCounts = new Dictionary<Guid, List<TaskPhoto>>();
            foreach (var t in paged) photoCounts[t.Id] = await tasks.GetTaskPhotosAsync(t.Id, ct);
            return Results.Ok(new { total = rows.Count, page = number, pageSize = size, items = paged.Select(t => ToTaskDto(t, photoCounts[t.Id])) });
        });

        // Same parameters as /tasks, but never paged: the admin exports everything the filter matches.
        g.MapGet("/tasks/export.xlsx", async (DateOnly? from, DateOnly? to, string? name, string? client, string? status, string? reviewStatus, string? jobType, string? q, string? sort, string? direction, SqlServerTaskSource tasks, SettingsStore settings, CancellationToken ct) =>
        {
            var awaiting = string.Equals(reviewStatus, "awaiting", StringComparison.OrdinalIgnoreCase);
            var rows = await tasks.GetTasksAsync(new TaskFilter(from, to, name, client, awaiting ? "complete" : status, awaiting ? null : reviewStatus, jobType, q), ct);
            if (awaiting) rows = rows.Where(IsAwaitingReview).ToList();
            rows = SortTasks(rows, sort ?? "timeIn", direction ?? "desc");
            var counts = new Dictionary<Guid, int>();
            foreach (var t in rows) counts[t.Id] = (await tasks.GetTaskPhotosAsync(t.Id, ct)).Count;
            var bytes = await ExcelExport.BuildTasksAsync(settings, rows, counts, ct);
            return Results.File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"SSD-Tasks-{DateTime.Now:yyyyMMdd-HHmm}.xlsx");
        });

        g.MapGet("/tasks/{id:guid}", async (Guid id, SqlServerTaskSource tasks, CancellationToken ct) =>
        {
            var t = await tasks.GetTaskAsync(id, ct);
            if (t is null) return Results.NotFound();
            var photos = await tasks.GetTaskPhotosAsync(id, ct);
            return Results.Ok(new
            {
                task = ToTaskDto(t, photos),
                photos = photos.Select(p => new
                {
                    p.Id,
                    p.Kind,
                    capturedAt = p.CapturedAt,
                    imageUrl = $"/admin-api/tasks/{id}/photos/{p.Id}/image",
                    thumbUrl = $"/admin-api/tasks/{id}/photos/{p.Id}/thumb",
                }),
                reviews = (await tasks.GetTaskReviewsAsync(id, ct)).Select(r => new { r.Id, r.Decision, r.Reason, r.ReviewedBy, reviewedAt = r.ReviewedAt }),
                edits = (await tasks.GetTaskEditsAsync(id, ct)).Select(e => new { e.Id, e.Field, e.OldValue, e.NewValue, e.EditedBy, editedAt = e.EditedAt }),
            });
        });

        g.MapGet("/tasks/{taskId:guid}/photos/{photoId:guid}/image", async (Guid taskId, Guid photoId, SqlServerTaskSource tasks, SettingsStore settings, CancellationToken ct) =>
        {
            var path = await TaskImagePath(settings, tasks, taskId, photoId, ct);
            return path is null ? Results.NotFound() : Results.File(path, "image/jpeg");
        });

        g.MapGet("/tasks/{taskId:guid}/photos/{photoId:guid}/thumb", async (Guid taskId, Guid photoId, SqlServerTaskSource tasks, SettingsStore settings, CancellationToken ct) =>
        {
            var path = await TaskImagePath(settings, tasks, taskId, photoId, ct);
            if (path is null) return Results.NotFound();
            var thumb = await Thumbnails.GetAsync(settings, photoId, path, ct);
            return Results.File(thumb ?? path, "image/jpeg");
        });

        g.MapPut("/tasks/{id:guid}", async (Guid id, TaskEditRequest body, SqlServerTaskSource tasks, CancellationToken ct) =>
        {
            try
            {
                var edit = new TaskFieldEdit(body.Client, body.JobType, body.OldImei, body.NewImei, body.OldSim, body.NewSim, body.PlateNumber, body.Note);
                return await tasks.UpdateTaskFieldsAsync(id, edit, Environment.UserName, ct) ? Results.Ok() : Results.NotFound();
            }
            catch (ArgumentException ex) { return Results.BadRequest(ex.Message); }
        });

        g.MapPost("/tasks/{id:guid}/approve", (Guid id, SqlServerTaskSource tasks, CancellationToken ct) => Review(tasks, id, "approved", null, ct));
        g.MapPost("/tasks/{id:guid}/reject", (Guid id, RejectRequest body, SqlServerTaskSource tasks, CancellationToken ct) => Review(tasks, id, "rejected", body.Reason, ct));

        g.MapDelete("/tasks/{id:guid}", async (Guid id, SqlServerTaskSource tasks, SettingsStore settings, CancellationToken ct) =>
        {
            var (found, photos) = await tasks.DeleteTaskAsync(id, settings.PhotosDir, settings.ThumbsDir, ct);
            if (!found) return Results.NotFound();
            store.Log($"Deleted task {id} and {photos} photo(s) by {Environment.UserName}.");
            return Results.Ok(new { id, photosDeleted = photos });
        });

        g.MapGet("/activities", () => Results.Ok(Activities.All));

        g.MapGet("/settings", async (SqlServerPhotoSource source, SqlServerTaskSource tasks, CancellationToken ct) =>
        {
            var ts = Tailscale.Status(store.Current.ApiPort, store.Current.AdminPort);
            var phoneUrl = ts.DnsName is null ? null : $"https://{ts.DnsName}";
            var remoteAdminUrl = ts.DnsName is null ? null : $"https://{ts.DnsName}:8443";
            // Counted here, not via CountPendingReviewAsync: a freshly completed task still has ReviewStatus 'none'.
            var completed = await tasks.GetTasksAsync(new TaskFilter(null, null, null, null, "complete", null, null, null), ct);
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
                taskCount = await tasks.CountAsync(ct),
                pendingReviewCount = completed.Count(IsAwaitingReview),
                reviewerName = Environment.UserName,
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

    static bool IsAwaitingReview(FieldTask t) => t.Status == "complete" && t.ReviewStatus is "none" or "pending";

    static async Task<IResult> Review(SqlServerTaskSource tasks, Guid id, string decision, string? reason, CancellationToken ct)
    {
        try
        {
            if (!await tasks.ReviewTaskAsync(id, decision, reason, Environment.UserName, ct)) return Results.NotFound();
            var t = await tasks.GetTaskAsync(id, ct);
            // The resulting state goes back so the UI can reconcile instead of guessing after a double click.
            return Results.Ok(new { id, reviewStatus = t?.ReviewStatus, reviewReason = t?.ReviewReason, reviewedAt = t?.ReviewedAt, reviewedBy = t?.ReviewedBy });
        }
        catch (ArgumentException ex) { return Results.BadRequest(ex.Message); }
    }

    static List<FieldTask> SortTasks(List<FieldTask> rows, string sort, string direction)
    {
        var asc = direction == "asc";
        Func<FieldTask, string> key = sort.ToLowerInvariant() switch
        {
            "technician" => t => t.TechnicianName ?? "",
            "client" => t => t.Client ?? "",
            "jobtype" => t => t.JobType ?? "",
            "status" => t => t.Status ?? "",
            "review" => t => t.ReviewStatus ?? "",
            _ => t => $"{t.TimeInDate:yyyy-MM-dd}T{t.TimeInTime:HH:mm:ss}",
        };
        var ordered = asc ? rows.OrderBy(key, StringComparer.OrdinalIgnoreCase) : rows.OrderByDescending(key, StringComparer.OrdinalIgnoreCase);
        return ordered.ToList();
    }

    static readonly Dictionary<string, string> StatusLabels = new()
    {
        ["timed_in"] = "Timed in",
        ["at_client"] = "At client",
        ["job_saved"] = "Job saved",
        ["complete"] = "Complete",
    };

    static object? Leg(double? lat, double? lon, double? accuracy, string? address, DateTimeOffset? capturedAt, DateOnly? date, TimeOnly? time)
    {
        if (capturedAt is null && date is null) return null;
        return new
        {
            latitude = lat,
            longitude = lon,
            accuracy,
            address,
            capturedAt,
            date = date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            time = time?.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            mapsUrl = lat is null || lon is null ? null : string.Create(CultureInfo.InvariantCulture, $"https://www.google.com/maps?q={lat:0.######},{lon:0.######}"),
        };
    }

    static object ToTaskDto(FieldTask t, List<TaskPhoto> photos)
    {
        var representative = photos.FirstOrDefault(p => p.Kind == "selfie_timein") ?? photos.FirstOrDefault();
        return new
        {
            t.Id,
            t.TechnicianName,
            t.Status,
            statusLabel = StatusLabels.TryGetValue(t.Status ?? "", out var label) ? label : t.Status,
            t.Client,
            t.JobType,
            t.OldImei,
            t.NewImei,
            t.OldSim,
            t.NewSim,
            t.PlateNumber,
            t.Note,
            timeIn = Leg(t.TimeInLatitude, t.TimeInLongitude, t.TimeInAccuracy, t.TimeInAddress, t.TimeInCapturedAt, t.TimeInDate, t.TimeInTime),
            atClient = Leg(t.ClientLatitude, t.ClientLongitude, t.ClientAccuracy, t.ClientAddress, t.ClientCapturedAt, t.ClientDate, t.ClientTime),
            timeOut = Leg(t.TimeOutLatitude, t.TimeOutLongitude, t.TimeOutAccuracy, t.TimeOutAddress, t.TimeOutCapturedAt, t.TimeOutDate, t.TimeOutTime),
            t.ReviewStatus,
            t.ReviewReason,
            reviewedAt = t.ReviewedAt,
            reviewedBy = t.ReviewedBy,
            pendingReview = IsAwaitingReview(t),
            photoCount = photos.Count,
            workPhotoCount = photos.Count(p => p.Kind == "extra"),
            thumbUrl = representative is null ? null : $"/admin-api/tasks/{t.Id}/photos/{representative.Id}/thumb",
            receivedAt = t.ReceivedAt,
            updatedAt = t.UpdatedAt,
        };
    }

    static async Task<string?> TaskImagePath(SettingsStore store, SqlServerTaskSource tasks, Guid taskId, Guid photoId, CancellationToken ct)
    {
        var photo = (await tasks.GetTaskPhotosAsync(taskId, ct)).FirstOrDefault(p => p.Id == photoId);
        if (photo is null) return null;
        var photosRoot = Path.GetFullPath(store.PhotosDir);
        var full = Path.GetFullPath(Path.Combine(photosRoot, photo.ImagePath));
        if (!full.StartsWith(photosRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(full)) return null;
        return full;
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
