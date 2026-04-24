namespace DiskSpaceInspector.Core.Models;

public sealed class DriveDashboard
{
    public Guid? ScanId { get; init; }

    public string RootPath { get; init; } = string.Empty;

    public ScanStatus Status { get; init; } = ScanStatus.Running;

    public DateTimeOffset? StartedAtUtc { get; init; }

    public DateTimeOffset? CompletedAtUtc { get; init; }

    public long TotalPhysicalBytes { get; init; }

    public long FilesScanned { get; init; }

    public long DirectoriesScanned { get; init; }

    public int IssueCount { get; init; }

    public long ReclaimableBytes { get; init; }

    public IReadOnlyList<FileSystemNode> TopSpaceConsumers { get; init; } = [];

    public IReadOnlyList<StorageBreakdownItem> CleanupBySafety { get; init; } = [];
}
