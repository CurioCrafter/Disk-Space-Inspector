namespace DiskSpaceInspector.Core.Models;

public sealed class ScanCheckpoint
{
    public Guid ScanId { get; init; }

    public string VolumeRootPath { get; init; } = string.Empty;

    public DateTimeOffset UpdatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public long LastNodeId { get; init; }

    public long FilesScanned { get; init; }

    public long DirectoriesScanned { get; init; }

    public long AccountedBytes { get; init; }

    public int QueueDepth { get; init; }

    public ScanStatus Status { get; init; }
}
