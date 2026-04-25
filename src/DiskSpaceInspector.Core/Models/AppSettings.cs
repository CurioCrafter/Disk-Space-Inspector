namespace DiskSpaceInspector.Core.Models;

public sealed class AppSettings
{
    public LaunchSettings Launch { get; set; } = new();

    public ScanDefaultSettings Scan { get; set; } = new();

    public ChartDisplaySettings Charts { get; set; } = new();

    public CleanupReviewSettings Cleanup { get; set; } = new();

    public PrivacySettings Privacy { get; set; } = new();

    public DataRetentionSettings Data { get; set; } = new();
}

public sealed class LaunchSettings
{
    public bool LoadLatestScanOnStartup { get; set; } = true;

    public bool ShowWelcomeWhenNoScan { get; set; } = true;

    public bool OpenDemoWorkspaceOnStartup { get; set; }
}

public sealed class ScanDefaultSettings
{
    public bool IncludeFixedDrives { get; set; } = true;

    public bool IncludeRemovableDrives { get; set; } = true;

    public bool RecordPermissionGaps { get; set; } = true;

    public bool DoNotFollowDirectoryLinks { get; set; } = true;
}

public sealed class ChartDisplaySettings
{
    public double MinimumNodeSizeMegabytes { get; set; }

    public int MaxBestInsightCards { get; set; } = 16;

    public int MaxAdvancedCards { get; set; } = 32;

    public string Density { get; set; } = "Comfortable";
}

public sealed class CleanupReviewSettings
{
    public bool StageSafeItemsOnlyByDefault { get; set; }

    public bool ShowSystemCleanupRoutes { get; set; } = true;

    public bool RequireReviewBeforeCleanup { get; set; } = true;
}

public sealed class PrivacySettings
{
    public bool RedactUserProfileInReports { get; set; } = true;

    public bool IncludeRelationshipsInReports { get; set; } = true;

    public bool IncludeInsightsInReports { get; set; } = true;
}

public sealed class DataRetentionSettings
{
    public bool RetainScanHistory { get; set; } = true;

    public int MaxSnapshotsToKeep { get; set; } = 12;
}
