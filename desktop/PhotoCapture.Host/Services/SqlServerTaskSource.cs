using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;
using PhotoCapture.Host.Data;

namespace PhotoCapture.Host.Services;

/// <summary>Task lifecycle storage in SSD_mobileCamera: upload from the phone, review and edits from the admin page.</summary>
public sealed class SqlServerTaskSource(string connectionString)
{
    static readonly string[] StatusOrder = ["timed_in", "at_client", "job_saved", "complete"];

    const string SelectTasks = """
        SELECT Id, TechnicianName, Status, Client, JobType, OldImei, NewImei, OldSim, NewSim, PlateNumber, Note,
               TimeInLatitude, TimeInLongitude, TimeInAccuracy, TimeInAddress, TimeInCapturedAt, TimeInDate, TimeInTime,
               ClientLatitude, ClientLongitude, ClientAccuracy, ClientAddress, ClientCapturedAt, ClientDate, ClientTime,
               TimeOutLatitude, TimeOutLongitude, TimeOutAccuracy, TimeOutAddress, TimeOutCapturedAt, TimeOutDate, TimeOutTime,
               ReviewStatus, ReviewReason, ReviewedAt, ReviewedBy, ReceivedAt, UpdatedAt
        FROM dbo.Tasks
        """;

    const string TaskFieldAssignments = """
        TechnicianName=@technician, Status=@status, Client=@client, JobType=@jobType,
        OldImei=@oldImei, NewImei=@newImei, OldSim=@oldSim, NewSim=@newSim, PlateNumber=@plate, Note=@note,
        TimeInLatitude=@tiLat, TimeInLongitude=@tiLon, TimeInAccuracy=@tiAcc, TimeInAddress=@tiAddr,
        TimeInCapturedAt=@tiAt, TimeInDate=@tiDate, TimeInTime=@tiTime,
        ClientLatitude=@clLat, ClientLongitude=@clLon, ClientAccuracy=@clAcc, ClientAddress=@clAddr,
        ClientCapturedAt=@clAt, ClientDate=@clDate, ClientTime=@clTime,
        TimeOutLatitude=@toLat, TimeOutLongitude=@toLon, TimeOutAccuracy=@toAcc, TimeOutAddress=@toAddr,
        TimeOutCapturedAt=@toAt, TimeOutDate=@toDate, TimeOutTime=@toTime,
        UpdatedAt=@updated
        """;

    /// <summary>Phone sync. Returns false when the write was ignored as stale, backwards or locked by an approval; both outcomes are success.</summary>
    public async Task<bool> UpsertTaskAsync(TaskUpsert payload, CancellationToken ct)
    {
        if (!Guid.TryParse(payload.Id, out var id) || id == Guid.Empty) throw new ArgumentException("id must be a non-empty UUID.");
        if (string.IsNullOrWhiteSpace(payload.TechnicianName) || payload.TechnicianName.Trim().Length > 200) throw new ArgumentException("technician_name is required (max 200 characters).");
        var status = payload.Status?.Trim() ?? "";
        if (Array.IndexOf(StatusOrder, status) < 0) throw new ArgumentException("status must be one of timed_in, at_client, job_saved, complete.");
        var client = Limit("client", payload.Client, 200);
        var jobType = Limit("job_type", payload.JobType, 50);
        if (status is "at_client" or "job_saved" or "complete" && client is null) throw new ArgumentException("client is required once the technician has arrived at the client.");
        if (status is "job_saved" or "complete" && jobType is null) throw new ArgumentException("job_type is required once the job form is saved.");
        if (jobType is not null && !Activities.IsValid(jobType)) throw new ArgumentException("job_type must be one of the allowed values.");
        var oldImei = Limit("old_imei", payload.OldImei, 50);
        var newImei = Limit("new_imei", payload.NewImei, 50);
        var oldSim = Limit("old_sim", payload.OldSim, 50);
        var newSim = Limit("new_sim", payload.NewSim, 50);
        var plate = Limit("plate_number", payload.PlateNumber, 20);
        var note = Limit("note", payload.Note, 2000);
        var timeInAddress = Limit("time_in_address", payload.TimeInAddress, 500);
        var clientAddress = Limit("client_address", payload.ClientAddress, 500);
        var timeOutAddress = Limit("time_out_address", payload.TimeOutAddress, 500);
        ValidateFix("time_in", payload.TimeInLatitude, payload.TimeInLongitude, payload.TimeInAccuracy);
        ValidateFix("client", payload.ClientLatitude, payload.ClientLongitude, payload.ClientAccuracy);
        ValidateFix("time_out", payload.TimeOutLatitude, payload.TimeOutLongitude, payload.TimeOutAccuracy);
        var timeIn = ValidateStamp("time_in", payload.TimeInCapturedAt, payload.TimeInDate, payload.TimeInTime);
        var atClient = ValidateStamp("client", payload.ClientCapturedAt, payload.ClientDate, payload.ClientTime);
        var timeOut = ValidateStamp("time_out", payload.TimeOutCapturedAt, payload.TimeOutDate, payload.TimeOutTime);
        // GPS can genuinely fail indoors; the timestamp proves the time out happened, so only that is mandatory.
        if (status == "complete" && timeOut.At is null) throw new ArgumentException("time_out timestamp is required when status is complete.");
        if (!DateTimeOffset.TryParse(payload.UpdatedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var updatedAt)) throw new ArgumentException("updated_at must be an ISO-8601 timestamp.");

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct);
        try
        {
            string? storedStatus = null, storedReview = null;
            DateTimeOffset storedUpdatedAt = default;
            await using (var current = new SqlCommand("SELECT Status, ReviewStatus, UpdatedAt FROM dbo.Tasks WITH (UPDLOCK, HOLDLOCK) WHERE Id=@id", connection, transaction))
            {
                current.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = id;
                await using var reader = await current.ExecuteReaderAsync(CommandBehavior.SequentialAccess, ct);
                if (await reader.ReadAsync(ct))
                {
                    storedStatus = reader.GetString(0);
                    storedReview = reader.GetString(1);
                    storedUpdatedAt = reader.GetFieldValue<DateTimeOffset>(2);
                }
            }
            if (storedStatus is not null)
            {
                if (updatedAt <= storedUpdatedAt) { await transaction.CommitAsync(ct); return false; }
                if (storedReview == "approved") { await transaction.CommitAsync(ct); return false; }
                // Status never moves backwards; a late sync of an earlier step still contributes its other fields.
                if (Array.IndexOf(StatusOrder, status) < Array.IndexOf(StatusOrder, storedStatus)) status = storedStatus;
            }

            var sql = storedStatus is null
                ? $"INSERT INTO dbo.Tasks (Id,TechnicianName,Status,Client,JobType,OldImei,NewImei,OldSim,NewSim,PlateNumber,Note,TimeInLatitude,TimeInLongitude,TimeInAccuracy,TimeInAddress,TimeInCapturedAt,TimeInDate,TimeInTime,ClientLatitude,ClientLongitude,ClientAccuracy,ClientAddress,ClientCapturedAt,ClientDate,ClientTime,TimeOutLatitude,TimeOutLongitude,TimeOutAccuracy,TimeOutAddress,TimeOutCapturedAt,TimeOutDate,TimeOutTime,ReviewStatus,ReceivedAt,UpdatedAt) VALUES (@id,@technician,@status,@client,@jobType,@oldImei,@newImei,@oldSim,@newSim,@plate,@note,@tiLat,@tiLon,@tiAcc,@tiAddr,@tiAt,@tiDate,@tiTime,@clLat,@clLon,@clAcc,@clAddr,@clAt,@clDate,@clTime,@toLat,@toLon,@toAcc,@toAddr,@toAt,@toDate,@toTime,'none',@received,@updated)"
                : $"UPDATE dbo.Tasks SET {TaskFieldAssignments} WHERE Id=@id";
            await using var write = new SqlCommand(sql, connection, transaction);
            write.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = id;
            write.Parameters.Add("@technician", SqlDbType.NVarChar, 200).Value = payload.TechnicianName.Trim();
            write.Parameters.Add("@status", SqlDbType.NVarChar, 20).Value = status;
            write.Parameters.Add("@client", SqlDbType.NVarChar, 200).Value = Db(client);
            write.Parameters.Add("@jobType", SqlDbType.NVarChar, 50).Value = Db(jobType);
            write.Parameters.Add("@oldImei", SqlDbType.NVarChar, 50).Value = Db(oldImei);
            write.Parameters.Add("@newImei", SqlDbType.NVarChar, 50).Value = Db(newImei);
            write.Parameters.Add("@oldSim", SqlDbType.NVarChar, 50).Value = Db(oldSim);
            write.Parameters.Add("@newSim", SqlDbType.NVarChar, 50).Value = Db(newSim);
            write.Parameters.Add("@plate", SqlDbType.NVarChar, 20).Value = Db(plate);
            write.Parameters.Add("@note", SqlDbType.NVarChar, 2000).Value = Db(note);
            AddFix(write, "ti", payload.TimeInLatitude, payload.TimeInLongitude, payload.TimeInAccuracy, timeInAddress, timeIn);
            AddFix(write, "cl", payload.ClientLatitude, payload.ClientLongitude, payload.ClientAccuracy, clientAddress, atClient);
            AddFix(write, "to", payload.TimeOutLatitude, payload.TimeOutLongitude, payload.TimeOutAccuracy, timeOutAddress, timeOut);
            write.Parameters.Add("@updated", SqlDbType.DateTimeOffset).Value = updatedAt;
            if (storedStatus is null) write.Parameters.Add("@received", SqlDbType.DateTimeOffset).Value = DateTimeOffset.UtcNow;
            await write.ExecuteNonQueryAsync(ct);
            await transaction.CommitAsync(ct);
            return true;
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<bool> SaveTaskPhotoAsync(string id, Guid taskId, string kind, string capturedAtRaw, IFormFile image, string storageRoot, long maxBytes, CancellationToken ct)
    {
        if (!Guid.TryParse(id, out var photoId) || photoId == Guid.Empty) throw new ArgumentException("id must be a non-empty UUID.");
        if (taskId == Guid.Empty) throw new ArgumentException("task_id must be a non-empty UUID.");
        kind = kind?.Trim() ?? "";
        if (kind is not ("selfie_timein" or "selfie_client" or "selfie_timeout" or "extra")) throw new ArgumentException("kind must be one of selfie_timein, selfie_client, selfie_timeout, extra.");
        if (image is null || image.Length == 0 || image.Length > maxBytes) throw new ArgumentException($"image must be 1..{maxBytes} bytes.");
        if (!string.Equals(image.ContentType, "image/jpeg", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Only JPEG images are accepted.");
        if (!DateTimeOffset.TryParse(capturedAtRaw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var capturedAt) || capturedAt.Offset != TimeSpan.FromHours(8)) throw new ArgumentException("captured_at must be an ISO-8601 timestamp with +08:00 offset.");
        var header = new byte[3];
        await using (var stream = image.OpenReadStream())
            if (await stream.ReadAsync(header, ct) != 3 || header[0] != 0xFF || header[1] != 0xD8 || header[2] != 0xFF) throw new ArgumentException("File is not a JPEG image.");

        var date = DateOnly.FromDateTime(capturedAt.DateTime);
        var folder = Path.Combine(storageRoot, date.Year.ToString("0000"), date.Month.ToString("00"), date.Day.ToString("00"));
        Directory.CreateDirectory(folder);
        var fullPath = Path.Combine(folder, $"{photoId:N}.jpg");
        var relativePath = Path.GetRelativePath(storageRoot, fullPath);
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct);
        try
        {
            // Photos are accepted even after approval: a technician can be offline with unsent photos when the admin approves, and refusing would destroy them permanently.
            await using (var parent = new SqlCommand("SELECT COUNT(*) FROM dbo.Tasks WHERE Id=@task", connection, transaction))
            {
                parent.Parameters.Add("@task", SqlDbType.UniqueIdentifier).Value = taskId;
                if (Convert.ToInt32(await parent.ExecuteScalarAsync(ct)) == 0) throw new ArgumentException("Unknown task_id.");
            }
            await using (var exists = new SqlCommand("SELECT COUNT(*) FROM dbo.TaskPhotos WHERE Id=@id", connection, transaction))
            {
                exists.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = photoId;
                if (Convert.ToInt32(await exists.ExecuteScalarAsync(ct)) > 0) return false;
            }
            await using (var output = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                await image.CopyToAsync(output, ct);
            await using var insert = new SqlCommand("INSERT INTO dbo.TaskPhotos (Id,TaskId,Kind,ImagePath,CapturedAt,ReceivedAt) VALUES (@id,@task,@kind,@path,@captured,@received)", connection, transaction);
            insert.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = photoId;
            insert.Parameters.Add("@task", SqlDbType.UniqueIdentifier).Value = taskId;
            insert.Parameters.Add("@kind", SqlDbType.NVarChar, 20).Value = kind;
            insert.Parameters.Add("@path", SqlDbType.NVarChar, 500).Value = relativePath;
            insert.Parameters.Add("@captured", SqlDbType.DateTimeOffset).Value = capturedAt;
            insert.Parameters.Add("@received", SqlDbType.DateTimeOffset).Value = DateTimeOffset.UtcNow;
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

    public async Task<List<FieldTask>> GetTasksAsync(TaskFilter filter, CancellationToken ct)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand { Connection = connection };
        var sql = SelectTasks + " WHERE 1=1";
        if (filter.From is { } from)
        {
            sql += " AND TimeInDate >= @from";
            command.Parameters.Add("@from", SqlDbType.Date).Value = from.ToDateTime(TimeOnly.MinValue);
        }
        if (filter.To is { } to)
        {
            sql += " AND TimeInDate <= @to";
            command.Parameters.Add("@to", SqlDbType.Date).Value = to.ToDateTime(TimeOnly.MinValue);
        }
        if (!string.IsNullOrWhiteSpace(filter.TechnicianName))
        {
            sql += " AND TechnicianName LIKE @technician ESCAPE '\\'";
            command.Parameters.Add("@technician", SqlDbType.NVarChar, 200).Value = "%" + EscapeLike(filter.TechnicianName.Trim()) + "%";
        }
        if (!string.IsNullOrWhiteSpace(filter.Client))
        {
            sql += " AND Client LIKE @client ESCAPE '\\'";
            command.Parameters.Add("@client", SqlDbType.NVarChar, 200).Value = "%" + EscapeLike(filter.Client.Trim()) + "%";
        }
        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            sql += " AND Status = @status";
            command.Parameters.Add("@status", SqlDbType.NVarChar, 20).Value = filter.Status.Trim();
        }
        if (!string.IsNullOrWhiteSpace(filter.ReviewStatus))
        {
            sql += " AND ReviewStatus = @review";
            command.Parameters.Add("@review", SqlDbType.NVarChar, 20).Value = filter.ReviewStatus.Trim();
        }
        if (!string.IsNullOrWhiteSpace(filter.JobType))
        {
            sql += " AND JobType = @jobType";
            command.Parameters.Add("@jobType", SqlDbType.NVarChar, 50).Value = filter.JobType.Trim();
        }
        if (!string.IsNullOrWhiteSpace(filter.Q))
        {
            sql += " AND (TechnicianName LIKE @q ESCAPE '\\' OR Client LIKE @q ESCAPE '\\' OR Note LIKE @q ESCAPE '\\' OR TimeInAddress LIKE @q ESCAPE '\\' OR ClientAddress LIKE @q ESCAPE '\\' OR TimeOutAddress LIKE @q ESCAPE '\\')";
            command.Parameters.Add("@q", SqlDbType.NVarChar, 2000).Value = "%" + EscapeLike(filter.Q.Trim()) + "%";
        }
        command.CommandText = sql + " ORDER BY TimeInDate DESC, TimeInTime DESC";
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, ct);
        var rows = new List<FieldTask>();
        while (await reader.ReadAsync(ct)) rows.Add(ReadTask(reader));
        return rows;
    }

    public async Task<FieldTask?> GetTaskAsync(Guid id, CancellationToken ct)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand(SelectTasks + " WHERE Id = @id", connection);
        command.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = id;
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, ct);
        return await reader.ReadAsync(ct) ? ReadTask(reader) : null;
    }

    public async Task<List<TaskPhoto>> GetTaskPhotosAsync(Guid taskId, CancellationToken ct)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand("SELECT Id, TaskId, Kind, ImagePath, CapturedAt, ReceivedAt FROM dbo.TaskPhotos WHERE TaskId=@task ORDER BY CapturedAt", connection);
        command.Parameters.Add("@task", SqlDbType.UniqueIdentifier).Value = taskId;
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, ct);
        var rows = new List<TaskPhoto>();
        while (await reader.ReadAsync(ct))
            rows.Add(new TaskPhoto
            {
                Id = reader.GetGuid(0), TaskId = reader.GetGuid(1), Kind = reader.GetString(2), ImagePath = reader.GetString(3),
                CapturedAt = reader.GetFieldValue<DateTimeOffset>(4), ReceivedAt = reader.GetFieldValue<DateTimeOffset>(5),
            });
        return rows;
    }

    public async Task<List<TaskReview>> GetTaskReviewsAsync(Guid taskId, CancellationToken ct)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand("SELECT Id, TaskId, Decision, Reason, ReviewedBy, ReviewedAt FROM dbo.TaskReviews WHERE TaskId=@task ORDER BY Id", connection);
        command.Parameters.Add("@task", SqlDbType.UniqueIdentifier).Value = taskId;
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, ct);
        var rows = new List<TaskReview>();
        while (await reader.ReadAsync(ct))
            rows.Add(new TaskReview
            {
                Id = reader.GetInt64(0), TaskId = reader.GetGuid(1), Decision = reader.GetString(2),
                Reason = reader.IsDBNull(3) ? null : reader.GetString(3),
                ReviewedBy = reader.GetString(4), ReviewedAt = reader.GetFieldValue<DateTimeOffset>(5),
            });
        return rows;
    }

    public async Task<List<TaskEdit>> GetTaskEditsAsync(Guid taskId, CancellationToken ct)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand("SELECT Id, TaskId, Field, OldValue, NewValue, EditedBy, EditedAt FROM dbo.TaskEdits WHERE TaskId=@task ORDER BY Id", connection);
        command.Parameters.Add("@task", SqlDbType.UniqueIdentifier).Value = taskId;
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, ct);
        var rows = new List<TaskEdit>();
        while (await reader.ReadAsync(ct))
            rows.Add(new TaskEdit
            {
                Id = reader.GetInt64(0), TaskId = reader.GetGuid(1), Field = reader.GetString(2),
                OldValue = reader.IsDBNull(3) ? null : reader.GetString(3),
                NewValue = reader.IsDBNull(4) ? null : reader.GetString(4),
                EditedBy = reader.GetString(5), EditedAt = reader.GetFieldValue<DateTimeOffset>(6),
            });
        return rows;
    }

    public async Task<int> CountAsync(CancellationToken ct)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand("SELECT COUNT(*) FROM dbo.Tasks", connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync(ct));
    }

    public async Task<int> CountPendingReviewAsync(CancellationToken ct)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand("SELECT COUNT(*) FROM dbo.Tasks WHERE Status='complete' AND ReviewStatus='pending'", connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync(ct));
    }

    public async Task<List<TaskReviewState>> GetReviewStatesAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        var rows = new List<TaskReviewState>();
        if (ids.Count == 0) return rows;
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        foreach (var chunk in ids.Distinct().Chunk(200))
        {
            await using var command = new SqlCommand { Connection = connection };
            var names = new List<string>(chunk.Length);
            for (var i = 0; i < chunk.Length; i++)
            {
                names.Add($"@id{i}");
                command.Parameters.Add($"@id{i}", SqlDbType.UniqueIdentifier).Value = chunk[i];
            }
            command.CommandText = $"SELECT Id, ReviewStatus, ReviewReason, ReviewedAt FROM dbo.Tasks WHERE Id IN ({string.Join(",", names)})";
            await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, ct);
            while (await reader.ReadAsync(ct))
                rows.Add(new TaskReviewState(reader.GetGuid(0), reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetFieldValue<DateTimeOffset>(3)));
        }
        return rows;
    }

    public async Task<bool> UpdateTaskFieldsAsync(Guid id, TaskFieldEdit edit, string editedBy, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(editedBy) || editedBy.Trim().Length > 200) throw new ArgumentException("editedBy is required (max 200 characters).");
        var values = new (string Field, string? Value)[]
        {
            ("Client", Limit("client", edit.Client, 200)),
            ("JobType", Limit("job_type", edit.JobType, 50)),
            ("OldImei", Limit("old_imei", edit.OldImei, 50)),
            ("NewImei", Limit("new_imei", edit.NewImei, 50)),
            ("OldSim", Limit("old_sim", edit.OldSim, 50)),
            ("NewSim", Limit("new_sim", edit.NewSim, 50)),
            ("PlateNumber", Limit("plate_number", edit.PlateNumber, 20)),
            ("Note", Limit("note", edit.Note, 2000)),
        };
        var jobType = values[1].Value;
        if (jobType is not null && !Activities.IsValid(jobType)) throw new ArgumentException("job_type must be one of the allowed values.");

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct);
        try
        {
            string?[] old;
            bool found;
            await using (var current = new SqlCommand("SELECT Client, JobType, OldImei, NewImei, OldSim, NewSim, PlateNumber, Note FROM dbo.Tasks WITH (UPDLOCK, HOLDLOCK) WHERE Id=@id", connection, transaction))
            {
                current.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = id;
                await using var reader = await current.ExecuteReaderAsync(CommandBehavior.SequentialAccess, ct);
                found = await reader.ReadAsync(ct);
                old = new string?[values.Length];
                if (found) for (var i = 0; i < values.Length; i++) old[i] = reader.IsDBNull(i) ? null : reader.GetString(i);
            }
            // Commit only after the reader is disposed; committing with it open throws on this connection.
            if (!found) { await transaction.CommitAsync(ct); return false; }
            var editedAt = DateTimeOffset.UtcNow;
            await using (var update = new SqlCommand("UPDATE dbo.Tasks SET Client=@client, JobType=@jobType, OldImei=@oldImei, NewImei=@newImei, OldSim=@oldSim, NewSim=@newSim, PlateNumber=@plate, Note=@note, UpdatedAt=@updated WHERE Id=@id", connection, transaction))
            {
                update.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = id;
                update.Parameters.Add("@client", SqlDbType.NVarChar, 200).Value = Db(values[0].Value);
                update.Parameters.Add("@jobType", SqlDbType.NVarChar, 50).Value = Db(values[1].Value);
                update.Parameters.Add("@oldImei", SqlDbType.NVarChar, 50).Value = Db(values[2].Value);
                update.Parameters.Add("@newImei", SqlDbType.NVarChar, 50).Value = Db(values[3].Value);
                update.Parameters.Add("@oldSim", SqlDbType.NVarChar, 50).Value = Db(values[4].Value);
                update.Parameters.Add("@newSim", SqlDbType.NVarChar, 50).Value = Db(values[5].Value);
                update.Parameters.Add("@plate", SqlDbType.NVarChar, 20).Value = Db(values[6].Value);
                update.Parameters.Add("@note", SqlDbType.NVarChar, 2000).Value = Db(values[7].Value);
                update.Parameters.Add("@updated", SqlDbType.DateTimeOffset).Value = editedAt;
                await update.ExecuteNonQueryAsync(ct);
            }
            for (var i = 0; i < values.Length; i++)
            {
                if (old[i] == values[i].Value) continue;
                await using var history = new SqlCommand("INSERT INTO dbo.TaskEdits (TaskId,Field,OldValue,NewValue,EditedBy,EditedAt) VALUES (@task,@field,@old,@new,@by,@at)", connection, transaction);
                history.Parameters.Add("@task", SqlDbType.UniqueIdentifier).Value = id;
                history.Parameters.Add("@field", SqlDbType.NVarChar, 50).Value = values[i].Field;
                history.Parameters.Add("@old", SqlDbType.NVarChar, 2000).Value = Db(old[i]);
                history.Parameters.Add("@new", SqlDbType.NVarChar, 2000).Value = Db(values[i].Value);
                history.Parameters.Add("@by", SqlDbType.NVarChar, 200).Value = editedBy.Trim();
                history.Parameters.Add("@at", SqlDbType.DateTimeOffset).Value = editedAt;
                await history.ExecuteNonQueryAsync(ct);
            }
            await transaction.CommitAsync(ct);
            return true;
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<bool> ReviewTaskAsync(Guid id, string decision, string? reason, string reviewedBy, CancellationToken ct)
    {
        decision = decision?.Trim() ?? "";
        if (decision is not ("approved" or "rejected")) throw new ArgumentException("decision must be approved or rejected.");
        if (string.IsNullOrWhiteSpace(reviewedBy) || reviewedBy.Trim().Length > 200) throw new ArgumentException("reviewedBy is required (max 200 characters).");
        var trimmedReason = Limit("reason", reason, 500);
        if (decision == "rejected" && trimmedReason is null) throw new ArgumentException("reason is required (max 500 characters) when rejecting.");
        if (decision == "approved") trimmedReason = null;

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct);
        try
        {
            string status;
            await using (var current = new SqlCommand("SELECT Status FROM dbo.Tasks WITH (UPDLOCK, HOLDLOCK) WHERE Id=@id", connection, transaction))
            {
                current.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = id;
                var found = await current.ExecuteScalarAsync(ct);
                if (found is null) { await transaction.CommitAsync(ct); return false; }
                status = (string)found;
            }
            if (status != "complete") throw new ArgumentException("Only completed tasks can be reviewed.");
            var reviewedAt = DateTimeOffset.UtcNow;
            await using (var update = new SqlCommand("UPDATE dbo.Tasks SET ReviewStatus=@decision, ReviewReason=@reason, ReviewedAt=@at, ReviewedBy=@by WHERE Id=@id", connection, transaction))
            {
                update.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = id;
                update.Parameters.Add("@decision", SqlDbType.NVarChar, 20).Value = decision;
                update.Parameters.Add("@reason", SqlDbType.NVarChar, 500).Value = Db(trimmedReason);
                update.Parameters.Add("@at", SqlDbType.DateTimeOffset).Value = reviewedAt;
                update.Parameters.Add("@by", SqlDbType.NVarChar, 200).Value = reviewedBy.Trim();
                await update.ExecuteNonQueryAsync(ct);
            }
            await using (var history = new SqlCommand("INSERT INTO dbo.TaskReviews (TaskId,Decision,Reason,ReviewedBy,ReviewedAt) VALUES (@task,@decision,@reason,@by,@at)", connection, transaction))
            {
                history.Parameters.Add("@task", SqlDbType.UniqueIdentifier).Value = id;
                history.Parameters.Add("@decision", SqlDbType.NVarChar, 20).Value = decision;
                history.Parameters.Add("@reason", SqlDbType.NVarChar, 500).Value = Db(trimmedReason);
                history.Parameters.Add("@by", SqlDbType.NVarChar, 200).Value = reviewedBy.Trim();
                history.Parameters.Add("@at", SqlDbType.DateTimeOffset).Value = reviewedAt;
                await history.ExecuteNonQueryAsync(ct);
            }
            await transaction.CommitAsync(ct);
            return true;
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<bool> ResubmitTaskAsync(Guid id, CancellationToken ct)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand("UPDATE dbo.Tasks SET ReviewStatus='pending', ReviewReason=NULL, UpdatedAt=@updated WHERE Id=@id AND ReviewStatus='rejected'", connection);
        command.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = id;
        command.Parameters.Add("@updated", SqlDbType.DateTimeOffset).Value = DateTimeOffset.UtcNow;
        return await command.ExecuteNonQueryAsync(ct) > 0;
    }

    /// <summary>Deletes a task and everything under it. Child rows go by CASCADE; the JPEG files have to be removed here or they are orphaned forever.</summary>
    public async Task<(bool Found, int PhotosDeleted)> DeleteTaskAsync(Guid id, string storageRoot, string thumbsRoot, CancellationToken ct)
    {
        var photos = await GetTaskPhotosAsync(id, ct);
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand("DELETE FROM dbo.Tasks WHERE Id=@id", connection);
        command.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = id;
        if (await command.ExecuteNonQueryAsync(ct) == 0) return (false, 0);

        // Files only after the row is gone: a failed delete must never leave a record pointing at a missing image.
        var root = Path.GetFullPath(storageRoot);
        foreach (var photo in photos)
        {
            try
            {
                var full = Path.GetFullPath(Path.Combine(root, photo.ImagePath));
                if (full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && File.Exists(full)) File.Delete(full);
                var thumb = Path.Combine(thumbsRoot, $"{photo.Id:N}.jpg");
                if (File.Exists(thumb)) File.Delete(thumb);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* a locked file leaves one orphan, not a failed delete */ }
        }
        return (true, photos.Count);
    }

    static void AddFix(SqlCommand command, string prefix, double? lat, double? lon, double? accuracy, string? address, (DateTimeOffset? At, DateOnly? Date, TimeOnly? Time) stamp)
    {
        command.Parameters.Add($"@{prefix}Lat", SqlDbType.Float).Value = lat.HasValue ? lat.Value : DBNull.Value;
        command.Parameters.Add($"@{prefix}Lon", SqlDbType.Float).Value = lon.HasValue ? lon.Value : DBNull.Value;
        command.Parameters.Add($"@{prefix}Acc", SqlDbType.Float).Value = accuracy.HasValue ? accuracy.Value : DBNull.Value;
        command.Parameters.Add($"@{prefix}Addr", SqlDbType.NVarChar, 500).Value = Db(address);
        command.Parameters.Add($"@{prefix}At", SqlDbType.DateTimeOffset).Value = stamp.At.HasValue ? stamp.At.Value : DBNull.Value;
        command.Parameters.Add($"@{prefix}Date", SqlDbType.Date).Value = stamp.Date.HasValue ? stamp.Date.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value;
        command.Parameters.Add($"@{prefix}Time", SqlDbType.Time).Value = stamp.Time.HasValue ? stamp.Time.Value.ToTimeSpan() : DBNull.Value;
    }

    static void ValidateFix(string prefix, double? lat, double? lon, double? accuracy)
    {
        if (lat is null && lon is null && accuracy is null) return;
        if (lat is null || lon is null || accuracy is null) throw new ArgumentException($"{prefix} latitude, longitude and accuracy must be supplied together.");
        if (!double.IsFinite(lat.Value) || lat is < -90 or > 90 || !double.IsFinite(lon.Value) || lon is < -180 or > 180 || !double.IsFinite(accuracy.Value) || accuracy < 0)
            throw new ArgumentException($"Invalid {prefix} latitude, longitude, or accuracy.");
    }

    static (DateTimeOffset? At, DateOnly? Date, TimeOnly? Time) ValidateStamp(string prefix, string? rawAt, string? rawDate, string? rawTime)
    {
        if (string.IsNullOrWhiteSpace(rawAt) && string.IsNullOrWhiteSpace(rawDate) && string.IsNullOrWhiteSpace(rawTime)) return (null, null, null);
        if (!DateTimeOffset.TryParse(rawAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at) || at.Offset != TimeSpan.FromHours(8)) throw new ArgumentException($"{prefix}_captured_at must be an ISO-8601 timestamp with +08:00 offset.");
        if (!DateOnly.TryParseExact(rawDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) || date != DateOnly.FromDateTime(at.DateTime)) throw new ArgumentException($"{prefix}_date must match {prefix}_captured_at in +08:00.");
        if (!TimeOnly.TryParseExact(rawTime, "HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) || time != TimeOnly.FromDateTime(at.DateTime)) throw new ArgumentException($"{prefix}_time must match {prefix}_captured_at in +08:00.");
        return (at, date, time);
    }

    static string? Limit(string field, string? value, int max)
    {
        var trimmed = NullIfBlank(value);
        if (trimmed is not null && trimmed.Length > max) throw new ArgumentException($"{field} must be at most {max} characters.");
        return trimmed;
    }

    static object Db(string? value) => value ?? (object)DBNull.Value;
    static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    static FieldTask ReadTask(SqlDataReader reader) => new()
    {
        Id = reader.GetGuid(0), TechnicianName = reader.GetString(1), Status = reader.GetString(2),
        Client = reader.IsDBNull(3) ? null : reader.GetString(3),
        JobType = reader.IsDBNull(4) ? null : reader.GetString(4),
        OldImei = reader.IsDBNull(5) ? null : reader.GetString(5),
        NewImei = reader.IsDBNull(6) ? null : reader.GetString(6),
        OldSim = reader.IsDBNull(7) ? null : reader.GetString(7),
        NewSim = reader.IsDBNull(8) ? null : reader.GetString(8),
        PlateNumber = reader.IsDBNull(9) ? null : reader.GetString(9),
        Note = reader.IsDBNull(10) ? null : reader.GetString(10),
        TimeInLatitude = reader.IsDBNull(11) ? null : reader.GetDouble(11),
        TimeInLongitude = reader.IsDBNull(12) ? null : reader.GetDouble(12),
        TimeInAccuracy = reader.IsDBNull(13) ? null : reader.GetDouble(13),
        TimeInAddress = reader.IsDBNull(14) ? null : reader.GetString(14),
        TimeInCapturedAt = reader.IsDBNull(15) ? null : reader.GetFieldValue<DateTimeOffset>(15),
        TimeInDate = reader.IsDBNull(16) ? null : DateOnly.FromDateTime(reader.GetDateTime(16)),
        TimeInTime = reader.IsDBNull(17) ? null : TimeOnly.FromTimeSpan(reader.GetTimeSpan(17)),
        ClientLatitude = reader.IsDBNull(18) ? null : reader.GetDouble(18),
        ClientLongitude = reader.IsDBNull(19) ? null : reader.GetDouble(19),
        ClientAccuracy = reader.IsDBNull(20) ? null : reader.GetDouble(20),
        ClientAddress = reader.IsDBNull(21) ? null : reader.GetString(21),
        ClientCapturedAt = reader.IsDBNull(22) ? null : reader.GetFieldValue<DateTimeOffset>(22),
        ClientDate = reader.IsDBNull(23) ? null : DateOnly.FromDateTime(reader.GetDateTime(23)),
        ClientTime = reader.IsDBNull(24) ? null : TimeOnly.FromTimeSpan(reader.GetTimeSpan(24)),
        TimeOutLatitude = reader.IsDBNull(25) ? null : reader.GetDouble(25),
        TimeOutLongitude = reader.IsDBNull(26) ? null : reader.GetDouble(26),
        TimeOutAccuracy = reader.IsDBNull(27) ? null : reader.GetDouble(27),
        TimeOutAddress = reader.IsDBNull(28) ? null : reader.GetString(28),
        TimeOutCapturedAt = reader.IsDBNull(29) ? null : reader.GetFieldValue<DateTimeOffset>(29),
        TimeOutDate = reader.IsDBNull(30) ? null : DateOnly.FromDateTime(reader.GetDateTime(30)),
        TimeOutTime = reader.IsDBNull(31) ? null : TimeOnly.FromTimeSpan(reader.GetTimeSpan(31)),
        ReviewStatus = reader.GetString(32),
        ReviewReason = reader.IsDBNull(33) ? null : reader.GetString(33),
        ReviewedAt = reader.IsDBNull(34) ? null : reader.GetFieldValue<DateTimeOffset>(34),
        ReviewedBy = reader.IsDBNull(35) ? null : reader.GetString(35),
        ReceivedAt = reader.GetFieldValue<DateTimeOffset>(36),
        UpdatedAt = reader.GetFieldValue<DateTimeOffset>(37),
    };

    static string EscapeLike(string value) => value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_").Replace("[", "\\[");
}
