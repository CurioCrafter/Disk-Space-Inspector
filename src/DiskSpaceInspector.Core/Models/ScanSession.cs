namespace DiskSpaceInspector.Core.Models;

public sealed class ScanSession
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string RootPath { get; init; } = string.Empty;

    public DateTimeOffset StartedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? CompletedAtUtc { get; set; }

    public ScanStatus Status { get; set; } = ScanStatus.Running;

    public long TotalLogicalBytes { get; set; }

    public long TotalPhysicalBytes { get; set; }

    public long FilesScanned { get; set; }

    public long DirectoriesScanned { get; set; }

    public int IssueCount { get; set; }
}
