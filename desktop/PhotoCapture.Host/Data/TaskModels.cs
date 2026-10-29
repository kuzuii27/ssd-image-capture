namespace PhotoCapture.Host.Data;

/// <summary>One technician job: time in, arrival at the client, the job form, time out, then an admin decision.</summary>
public sealed class FieldTask
{
    public Guid Id { get; set; }
    public string TechnicianName { get; set; } = "";
    public string Status { get; set; } = "";
    public string? Client { get; set; }
    public string? JobType { get; set; }
    public string? OldImei { get; set; }
    public string? NewImei { get; set; }
    public string? OldSim { get; set; }
    public string? NewSim { get; set; }
    public string? PlateNumber { get; set; }
    public string? Note { get; set; }

    public double? TimeInLatitude { get; set; }
    public double? TimeInLongitude { get; set; }
    public double? TimeInAccuracy { get; set; }
    public string? TimeInAddress { get; set; }
    public DateTimeOffset? TimeInCapturedAt { get; set; }
    public DateOnly? TimeInDate { get; set; }
    public TimeOnly? TimeInTime { get; set; }

    public double? ClientLatitude { get; set; }
    public double? ClientLongitude { get; set; }
    public double? ClientAccuracy { get; set; }
    public string? ClientAddress { get; set; }
    public DateTimeOffset? ClientCapturedAt { get; set; }
    public DateOnly? ClientDate { get; set; }
    public TimeOnly? ClientTime { get; set; }

    public double? TimeOutLatitude { get; set; }
    public double? TimeOutLongitude { get; set; }
    public double? TimeOutAccuracy { get; set; }
    public string? TimeOutAddress { get; set; }
    public DateTimeOffset? TimeOutCapturedAt { get; set; }
    public DateOnly? TimeOutDate { get; set; }
    public TimeOnly? TimeOutTime { get; set; }

    public string ReviewStatus { get; set; } = "none";
    public string? ReviewReason { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public string? ReviewedBy { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class TaskPhoto
{
    public Guid Id { get; set; }
    public Guid TaskId { get; set; }
    public string Kind { get; set; } = "";
    /// <summary>Path relative to the photos folder.</summary>
    public string ImagePath { get; set; } = "";
    public DateTimeOffset CapturedAt { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
}

/// <summary>One row per admin decision, append-only.</summary>
public sealed class TaskReview
{
    public long Id { get; set; }
    public Guid TaskId { get; set; }
    public string Decision { get; set; } = "";
    public string? Reason { get; set; }
    public string ReviewedBy { get; set; } = "";
    public DateTimeOffset ReviewedAt { get; set; }
}

/// <summary>One row per admin-changed field, append-only.</summary>
public sealed class TaskEdit
{
    public long Id { get; set; }
    public Guid TaskId { get; set; }
    public string Field { get; set; } = "";
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string EditedBy { get; set; } = "";
    public DateTimeOffset EditedAt { get; set; }
}

/// <summary>Phone-owned fields only; review fields belong to the server and are deliberately absent.</summary>
public sealed record TaskUpsert
{
    public string Id { get; init; } = "";
    public string TechnicianName { get; init; } = "";
    public string Status { get; init; } = "";
    public string? Client { get; init; }
    public string? JobType { get; init; }
    public string? OldImei { get; init; }
    public string? NewImei { get; init; }
    public string? OldSim { get; init; }
    public string? NewSim { get; init; }
    public string? PlateNumber { get; init; }
    public string? Note { get; init; }

    public double? TimeInLatitude { get; init; }
    public double? TimeInLongitude { get; init; }
    public double? TimeInAccuracy { get; init; }
    public string? TimeInAddress { get; init; }
    public string? TimeInCapturedAt { get; init; }
    public string? TimeInDate { get; init; }
    public string? TimeInTime { get; init; }

    public double? ClientLatitude { get; init; }
    public double? ClientLongitude { get; init; }
    public double? ClientAccuracy { get; init; }
    public string? ClientAddress { get; init; }
    public string? ClientCapturedAt { get; init; }
    public string? ClientDate { get; init; }
    public string? ClientTime { get; init; }

    public double? TimeOutLatitude { get; init; }
    public double? TimeOutLongitude { get; init; }
    public double? TimeOutAccuracy { get; init; }
    public string? TimeOutAddress { get; init; }
    public string? TimeOutCapturedAt { get; init; }
    public string? TimeOutDate { get; init; }
    public string? TimeOutTime { get; init; }

    /// <summary>The phone's own last-change stamp; an older or equal value is ignored as a stale sync.</summary>
    public string UpdatedAt { get; init; } = "";
}

public sealed record TaskFilter(DateOnly? From, DateOnly? To, string? TechnicianName, string? Client, string? Status, string? ReviewStatus, string? JobType, string? Q);

public sealed record TaskFieldEdit(string? Client, string? JobType, string? OldImei, string? NewImei, string? OldSim, string? NewSim, string? PlateNumber, string? Note);

public sealed record TaskReviewState(Guid Id, string ReviewStatus, string? ReviewReason, DateTimeOffset? ReviewedAt);
