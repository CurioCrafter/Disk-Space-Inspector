namespace DiskSpaceInspector.Core.Models;

public sealed class ScanProgress
{
    public Guid ScanId { get; init; }

    public string CurrentPath { get; init; } = string.Empty;

    public long FilesScanned { get; init; }

    public long DirectoriesScanned { get; init; }

    public long BytesSeen { get; init; }

    public long UsedBytes { get; init; }

    public double ProgressFraction { get; init; }

    public double FilesPerSecond { get; init; }

    public double DirectoriesPerSecond { get; init; }

    public TimeSpan Elapsed { get; init; }

    public TimeSpan? EstimatedRemaining { get; init; }

    public int InaccessibleCount { get; init; }

    public int QueueDepth { get; init; }

    public int BatchNumber { get; init; }

    public int VolumesCompleted { get; init; }

    public int VolumeCount { get; init; } = 1;

    public int Issues { get; init; }

    public string Message { get; init; } = string.Empty;
}
