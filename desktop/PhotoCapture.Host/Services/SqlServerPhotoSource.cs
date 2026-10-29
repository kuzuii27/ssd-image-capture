using System.Data;
using Microsoft.Data.SqlClient;
using PhotoCapture.Host.Data;

namespace PhotoCapture.Host.Services;

/// <summary>Read-only access to the existing SSD_mobileCamera database.</summary>
public sealed class SqlServerPhotoSource(string connectionString)
{
    const string SelectPhotos = """
        SELECT Id, Name, Client, Activity, Note, Address, Latitude, Longitude, Accuracy,
               CapturedAt, CaptureDate, CaptureTime, ReceivedAt, ImagePath, UpdatedAt
        FROM dbo.Photos
        """;

    public async Task<List<Photo>> GetPhotosAsync(PhotoFilter filter, CancellationToken ct)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand { Connection = connection };
        var sql = SelectPhotos + " WHERE 1=1";
        if (filter.From is { } from)
        {
            sql += " AND CaptureDate >= @from";
            command.Parameters.Add("@from", SqlDbType.Date).Value = from.ToDateTime(TimeOnly.MinValue);
        }
        if (filter.To is { } to)
        {
            sql += " AND CaptureDate <= @to";
            command.Parameters.Add("@to", SqlDbType.Date).Value = to.ToDateTime(TimeOnly.MinValue);
        }
        if (!string.IsNullOrWhiteSpace(filter.Name))
        {
            sql += " AND Name LIKE @name ESCAPE '\\'";
            command.Parameters.Add("@name", SqlDbType.NVarChar, 200).Value = "%" + EscapeLike(filter.Name.Trim()) + "%";
        }
        if (!string.IsNullOrWhiteSpace(filter.Client))
        {
            sql += " AND Client LIKE @client ESCAPE '\\'";
            command.Parameters.Add("@client", SqlDbType.NVarChar, 200).Value = "%" + EscapeLike(filter.Client.Trim()) + "%";
        }
        if (!string.IsNullOrWhiteSpace(filter.Q))
        {
            sql += " AND (Name LIKE @q ESCAPE '\\' OR Client LIKE @q ESCAPE '\\' OR Note LIKE @q ESCAPE '\\' OR Address LIKE @q ESCAPE '\\')";
            command.Parameters.Add("@q", SqlDbType.NVarChar, 2000).Value = "%" + EscapeLike(filter.Q.Trim()) + "%";
        }
        command.CommandText = sql + " ORDER BY CaptureDate, CaptureTime";
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, ct);
        var rows = new List<Photo>();
        while (await reader.ReadAsync(ct))
        {
            rows.Add(new Photo
            {
                Id = reader.GetGuid(0), Name = reader.GetString(1),
                Client = reader.IsDBNull(2) ? null : reader.GetString(2),
                Activity = reader.IsDBNull(3) ? null : reader.GetString(3),
                Note = reader.IsDBNull(4) ? null : reader.GetString(4),
                Address = reader.IsDBNull(5) ? null : reader.GetString(5),
                Latitude = reader.GetDouble(6), Longitude = reader.GetDouble(7), Accuracy = reader.GetDouble(8),
                CapturedAt = reader.GetFieldValue<DateTimeOffset>(9),
                CaptureDate = DateOnly.FromDateTime(reader.GetDateTime(10)),
                CaptureTime = TimeOnly.FromTimeSpan(reader.GetTimeSpan(11)),
                ReceivedAt = reader.GetFieldValue<DateTimeOffset>(12), ImagePath = reader.GetString(13),
                UpdatedAt = reader.IsDBNull(14) ? null : reader.GetFieldValue<DateTimeOffset>(14),
            });
        }
        return rows;
    }

    public async Task<int> CountAsync(CancellationToken ct)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand("SELECT COUNT(*) FROM dbo.Photos", connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync(ct));
    }

    public async Task<Photo?> GetPhotoAsync(Guid id, CancellationToken ct)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand(SelectPhotos + " WHERE Id = @id", connection);
        command.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = id;
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, ct);
        return await reader.ReadAsync(ct) ? ReadPhoto(reader) : null;
    }

    public async Task<bool> UpdatePhotoAsync(Guid id, string name, string? client, string? activity, string? note, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200) throw new ArgumentException("name is required (max 200 characters).");
        if (!string.IsNullOrWhiteSpace(activity) && !Activities.IsValid(activity.Trim())) throw new ArgumentException("Activity must be one of the allowed values.");
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand("UPDATE dbo.Photos SET Name=@name, Client=@client, Activity=@activity, Note=@note, UpdatedAt=@updated WHERE Id=@id", connection);
        command.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = id;
        command.Parameters.Add("@name", SqlDbType.NVarChar, 200).Value = name.Trim();
        command.Parameters.Add("@client", SqlDbType.NVarChar, 200).Value = NullIfBlank(client) ?? (object)DBNull.Value;
        command.Parameters.Add("@activity", SqlDbType.NVarChar, 50).Value = NullIfBlank(activity) ?? (object)DBNull.Value;
        command.Parameters.Add("@note", SqlDbType.NVarChar, 2000).Value = NullIfBlank(note) ?? (object)DBNull.Value;
        command.Parameters.Add("@updated", SqlDbType.DateTimeOffset).Value = DateTimeOffset.UtcNow;
        var affected = await command.ExecuteNonQueryAsync(ct);
        return affected > 0;
    }

    public async Task<bool> SavePhoneUploadAsync(UploadPhoto form, string storageRoot, long maxBytes, CancellationToken ct)
    {
        if (!Guid.TryParse(form.Id, out var id) || id == Guid.Empty) throw new ArgumentException("id must be a non-empty UUID.");
        if (string.IsNullOrWhiteSpace(form.Name) || form.Name.Trim().Length > 200) throw new ArgumentException("name is required (max 200 characters).");
        if (form.Image is null || form.Image.Length == 0 || form.Image.Length > maxBytes) throw new ArgumentException($"image must be 1..{maxBytes} bytes.");
        if (!string.Equals(form.Image.ContentType, "image/jpeg", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Only JPEG images are accepted.");
        if (!string.IsNullOrWhiteSpace(form.Activity) && !Activities.IsValid(form.Activity.Trim())) throw new ArgumentException("Activity must be one of the allowed values.");
        if (!double.IsFinite(form.Latitude) || form.Latitude is < -90 or > 90 || !double.IsFinite(form.Longitude) || form.Longitude is < -180 or > 180 || !double.IsFinite(form.Accuracy) || form.Accuracy < 0) throw new ArgumentException("Invalid latitude, longitude, or accuracy.");
        if (!DateTimeOffset.TryParse(form.captured_at, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var capturedAt) || capturedAt.Offset != TimeSpan.FromHours(8)) throw new ArgumentException("captured_at must be an ISO-8601 timestamp with +08:00 offset.");
        if (!DateOnly.TryParseExact(form.capture_date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var date) || date != DateOnly.FromDateTime(capturedAt.DateTime)) throw new ArgumentException("capture_date must match captured_at in +08:00.");
        if (!TimeOnly.TryParseExact(form.capture_time, "HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var time) || time != TimeOnly.FromDateTime(capturedAt.DateTime)) throw new ArgumentException("capture_time must match captured_at in +08:00.");
        var header = new byte[3];
        await using (var stream = form.Image.OpenReadStream())
            if (await stream.ReadAsync(header, ct) != 3 || header[0] != 0xFF || header[1] != 0xD8 || header[2] != 0xFF) throw new ArgumentException("File is not a JPEG image.");

        var folder = Path.Combine(storageRoot, date.Year.ToString("0000"), date.Month.ToString("00"), date.Day.ToString("00"));
        Directory.CreateDirectory(folder);
        var fullPath = Path.Combine(folder, $"{id:N}.jpg");
        var relativePath = Path.GetRelativePath(storageRoot, fullPath);
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct);
        try
        {
            await using (var exists = new SqlCommand("SELECT COUNT(*) FROM dbo.Photos WHERE Id=@id", connection, transaction))
            {
                exists.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = id;
                if (Convert.ToInt32(await exists.ExecuteScalarAsync(ct)) > 0) return false;
            }
            await using (var output = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                await form.Image.CopyToAsync(output, ct);
            await using var insert = new SqlCommand("INSERT INTO dbo.Photos (Id,Name,Latitude,Longitude,Accuracy,CapturedAt,CaptureDate,CaptureTime,ReceivedAt,ImagePath,Client,Activity,Note,Address) VALUES (@id,@name,@lat,@lon,@accuracy,@captured,@date,@time,@received,@path,@client,@activity,@note,@address)", connection, transaction);
            insert.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = id;
            insert.Parameters.Add("@name", SqlDbType.NVarChar, 200).Value = form.Name.Trim();
            insert.Parameters.Add("@lat", SqlDbType.Float).Value = form.Latitude;
            insert.Parameters.Add("@lon", SqlDbType.Float).Value = form.Longitude;
            insert.Parameters.Add("@accuracy", SqlDbType.Float).Value = form.Accuracy;
            insert.Parameters.Add("@captured", SqlDbType.DateTimeOffset).Value = capturedAt;
            insert.Parameters.Add("@date", SqlDbType.Date).Value = date.ToDateTime(TimeOnly.MinValue);
            insert.Parameters.Add("@time", SqlDbType.Time).Value = time.ToTimeSpan();
            insert.Parameters.Add("@received", SqlDbType.DateTimeOffset).Value = DateTimeOffset.UtcNow;
            insert.Parameters.Add("@path", SqlDbType.NVarChar, 500).Value = relativePath;
            insert.Parameters.Add("@client", SqlDbType.NVarChar, 200).Value = NullIfBlank(form.Client) ?? (object)DBNull.Value;
            insert.Parameters.Add("@activity", SqlDbType.NVarChar, 50).Value = NullIfBlank(form.Activity) ?? (object)DBNull.Value;
            insert.Parameters.Add("@note", SqlDbType.NVarChar, 2000).Value = NullIfBlank(form.Note) ?? (object)DBNull.Value;
            insert.Parameters.Add("@address", SqlDbType.NVarChar, 500).Value = NullIfBlank(form.Address) ?? (object)DBNull.Value;
            await insert.ExecuteNonQueryAsync(ct);
            await transaction.CommitAsync(ct);
            return true;
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            if (File.Exists(fullPath)) File.Delete(fullPath);
            throw;
        }
    }

    static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    static Photo ReadPhoto(SqlDataReader reader) => new()
    {
        Id = reader.GetGuid(0), Name = reader.GetString(1),
        Client = reader.IsDBNull(2) ? null : reader.GetString(2),
        Activity = reader.IsDBNull(3) ? null : reader.GetString(3),
        Note = reader.IsDBNull(4) ? null : reader.GetString(4),
        Address = reader.IsDBNull(5) ? null : reader.GetString(5),
        Latitude = reader.GetDouble(6), Longitude = reader.GetDouble(7), Accuracy = reader.GetDouble(8),
        CapturedAt = reader.GetFieldValue<DateTimeOffset>(9),
        CaptureDate = DateOnly.FromDateTime(reader.GetDateTime(10)),
        CaptureTime = TimeOnly.FromTimeSpan(reader.GetTimeSpan(11)),
        ReceivedAt = reader.GetFieldValue<DateTimeOffset>(12), ImagePath = reader.GetString(13),
        UpdatedAt = reader.IsDBNull(14) ? null : reader.GetFieldValue<DateTimeOffset>(14),
    };

    static string EscapeLike(string value) => value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_").Replace("[", "\\[");
}
