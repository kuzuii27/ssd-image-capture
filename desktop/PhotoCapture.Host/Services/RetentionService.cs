using Microsoft.EntityFrameworkCore;
using PhotoCapture.Host.Data;

namespace PhotoCapture.Host.Services;

public sealed record RetentionStatus(bool Enabled, int Days, string? Cutoff, int DueCount, DateTimeOffset? LastRun, int LastDeleted);

/// <summary>
/// Deletes photos (file, thumbnail, record, edit history) whose capture date is older than the retention period.
/// Photos that reached the server within the last day are always kept, so a phone that was offline
/// for weeks does not lose its pictures the moment it syncs.
/// </summary>
public sealed class RetentionService(SettingsStore store, IServiceScopeFactory scopes) : BackgroundService
{
    static readonly TimeSpan Interval = TimeSpan.FromHours(6);
    static readonly TimeSpan Grace = TimeSpan.FromDays(1);
    static readonly TimeSpan Plus8 = TimeSpan.FromHours(8);
    readonly SemaphoreSlim running = new(1, 1);

    public DateTimeOffset? LastRun { get; private set; }
    public int LastDeleted { get; private set; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken).ConfigureAwait(false);
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { store.Log($"Retention failed: {ex}"); }
            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    public static DateOnly Cutoff(int days) =>
        DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(Plus8).DateTime).AddDays(-days);

    static IQueryable<Photo> Due(PhotosDb db, int days)
    {
        var cutoff = Cutoff(days);
        var received = DateTimeOffset.UtcNow - Grace;
        return db.Photos.Where(p => p.CaptureDate < cutoff && p.ReceivedAt < received);
    }

    public async Task<RetentionStatus> StatusAsync(PhotosDb db, CancellationToken ct, int? previewDays = null)
    {
        var s = store.Current;
        var days = previewDays ?? s.RetentionDays;
        var due = await Due(db, days).CountAsync(ct);
        return new RetentionStatus(s.RetentionEnabled, days, Cutoff(days).ToString("yyyy-MM-dd"), due, LastRun, LastDeleted);
    }

    /// <summary>Returns how many photos were deleted.</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        if (!store.Current.RetentionEnabled) return 0;
        await running.WaitAsync(ct);
        try
        {
            var days = store.Current.RetentionDays;
            var deleted = 0;
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PhotosDb>();
            while (!ct.IsCancellationRequested)
            {
                var batch = await Due(db, days).OrderBy(p => p.CaptureDate).Take(200).ToListAsync(ct);
                if (batch.Count == 0) break;
                var ids = batch.Select(p => p.Id).ToList();
                db.PhotoEdits.RemoveRange(await db.PhotoEdits.Where(e => ids.Contains(e.PhotoId)).ToListAsync(ct));
                db.Photos.RemoveRange(batch);
                await db.SaveChangesAsync(ct);   // records first: a leftover file is harmless, a record without a file is not
                foreach (var p in batch) DeleteFiles(p);
                deleted += batch.Count;
                db.ChangeTracker.Clear();
            }
            LastRun = DateTimeOffset.Now;
            LastDeleted = deleted;
            if (deleted > 0)
            {
                PruneEmptyFolders(store.PhotosDir);
                store.Log($"Retention: deleted {deleted} photo(s) captured before {Cutoff(days):yyyy-MM-dd} ({days}-day rule).");
            }
            return deleted;
        }
        finally { running.Release(); }
    }

    void DeleteFiles(Photo p)
    {
        TryDelete(Path.Combine(store.ThumbsDir, $"{p.Id:N}.jpg"));
        var full = Path.GetFullPath(Path.Combine(store.PhotosDir, p.ImagePath));
        if (full.StartsWith(store.PhotosDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) TryDelete(full);
    }

    void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { store.Log($"Retention: could not delete {path}: {ex.Message}"); }
    }

    static void PruneEmptyFolders(string root)
    {
        try
        {
            foreach (var dir in Directory.GetDirectories(root, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
                if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
