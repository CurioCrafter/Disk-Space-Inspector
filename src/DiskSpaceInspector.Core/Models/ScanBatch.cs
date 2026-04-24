namespace DiskSpaceInspector.Core.Models;

public sealed class ScanBatch
{
    public Guid ScanId { get; init; }

    public int BatchNumber { get; init; }

    public DateTimeOffset CapturedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public List<FileSystemNode> Nodes { get; init; } = [];

    public List<FileSystemEdge> Edges { get; init; } = [];

    public List<StorageRelationship> Relationships { get; init; } = [];

    public List<ScanIssue> Issues { get; init; } = [];

    public ScanMetrics Metrics { get; init; } = new();
}
