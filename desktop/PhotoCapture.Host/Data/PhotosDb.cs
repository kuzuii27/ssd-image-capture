using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace PhotoCapture.Host.Data;

public sealed class Photo
{
    public Guid Id { get; set; }
    [MaxLength(200)] public string Name { get; set; } = "";
    [MaxLength(200)] public string? Client { get; set; }
    [MaxLength(50)] public string? Activity { get; set; }
    [MaxLength(2000)] public string? Note { get; set; }
    [MaxLength(500)] public string? Address { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double Accuracy { get; set; }
    public DateTimeOffset CapturedAt { get; set; }
    public DateOnly CaptureDate { get; set; }
    public TimeOnly CaptureTime { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    /// <summary>Path relative to the photos folder.</summary>
    [MaxLength(500)] public string ImagePath { get; set; } = "";
}

/// <summary>One row per changed field, so admin edits stay traceable.</summary>
public sealed class PhotoEdit
{
    public long Id { get; set; }
    public Guid PhotoId { get; set; }
    [MaxLength(50)] public string Field { get; set; } = "";
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public DateTimeOffset EditedAt { get; set; }
    [MaxLength(200)] public string EditedBy { get; set; } = "";
}

public sealed class PhotosDb(DbContextOptions<PhotosDb> options) : DbContext(options)
{
    public DbSet<Photo> Photos => Set<Photo>();
    public DbSet<PhotoEdit> PhotoEdits => Set<PhotoEdit>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // SQLite cannot sort/compare DateTimeOffset natively; store as ISO-8601 text.
        var dto = new ValueConverter<DateTimeOffset, string>(
            v => v.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffffzzz"),
            v => DateTimeOffset.Parse(v, System.Globalization.CultureInfo.InvariantCulture));
        var dtoNullable = new ValueConverter<DateTimeOffset?, string?>(
            v => v == null ? null : v.Value.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffffzzz"),
            v => v == null ? null : DateTimeOffset.Parse(v, System.Globalization.CultureInfo.InvariantCulture));

        var photo = modelBuilder.Entity<Photo>();
        photo.ToTable("Photos");
        photo.HasKey(p => p.Id);
        photo.HasIndex(p => new { p.CaptureDate, p.CaptureTime });
        photo.Property(p => p.CapturedAt).HasConversion(dto);
        photo.Property(p => p.ReceivedAt).HasConversion(dto);
        photo.Property(p => p.UpdatedAt).HasConversion(dtoNullable);
        photo.Property(p => p.Name).HasMaxLength(200).IsRequired();
        photo.Property(p => p.ImagePath).HasMaxLength(500).IsRequired();

        var edit = modelBuilder.Entity<PhotoEdit>();
        edit.ToTable("PhotoEdits");
        edit.HasKey(e => e.Id);
        edit.HasIndex(e => e.PhotoId);
        edit.Property(e => e.EditedAt).HasConversion(dto);
    }
}

/// <summary>
/// Creates the database on first run and upgrades older files in place.
/// Add a new step per schema change and bump <see cref="Latest"/>; never edit an old step.
/// </summary>
public static class Schema
{
    public const int Latest = 1;

    public static void Apply(PhotosDb db)
    {
        var version = db.Database.SqlQueryRaw<int>("SELECT user_version AS Value FROM pragma_user_version").AsEnumerable().First();
        if (version == 0)
        {
            db.Database.EnsureCreated();
            version = 1;
        }
        // if (version < 2) { db.Database.ExecuteSqlRaw("ALTER TABLE Photos ADD COLUMN ..."); version = 2; }
        db.Database.ExecuteSqlRaw($"PRAGMA user_version = {Latest}");
    }
}
