namespace PhotoCapture.Host;

/// <summary>Fixed list of activity types a technician can pick. Single source of truth for validation and the dropdown.</summary>
public static class Activities
{
    public static readonly string[] All =
    [
        "Installation",
        "Check up",
        "Installation/Check up",
        "Upgrade",
        "Pullout/Transfer",
        "Collection with OR",
        "Errand",
        "Transfer",
        "Pull out",
        "Meeting",
        "Dispatch",
        "Check up/Recalibrate",
        "Collection without OR",
        "Demo Installation",
        "Re-install",
        "Change device",
        "Office",
        "Migrate",
        "Replacement",
        "Platform training",
    ];

    public static bool IsValid(string? value) => value is not null && All.Contains(value);
}
