namespace DiskSpaceInspector.Core.Models;

public sealed class ScanMetrics
{
    public Guid ScanId { get; init; }

    public string VolumeRootPath { get; init; } = string.Empty;

    public string CurrentPath { get; init; } = string.Empty;

    public long UsedBytes { get; init; }

    public long AccountedBytes { get; init; }

    public double ProgressFraction => UsedBytes > 0 ? Math.Clamp(AccountedBytes / (double)UsedBytes, 0, 1) : 0;

    public long FilesScanned { get; init; }

    public long DirectoriesScanned { get; init; }

    public int InaccessibleCount { get; init; }

    public int QueueDepth { get; init; }

    public int BatchNumber { get; init; }

    public int VolumesCompleted { get; init; }

    public int VolumeCount { get; init; } = 1;

    public TimeSpan Elapsed { get; init; }

    public TimeSpan? EstimatedRemaining { get; init; }

    public double FilesPerSecond { get; init; }

    public double DirectoriesPerSecond { get; init; }

    public bool IsIndeterminate => UsedBytes <= 0;
}
