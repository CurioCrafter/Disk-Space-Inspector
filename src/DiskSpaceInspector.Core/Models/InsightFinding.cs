namespace DiskSpaceInspector.Core.Models;

public sealed class InsightFinding
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public Guid ScanId { get; init; }

    public long NodeId { get; init; }

    public string StableId { get; init; } = string.Empty;

    public string Path { get; init; } = string.Empty;

    public string Tool { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public CleanupSafety Safety { get; init; }

    public CleanupActionKind RecommendedAction { get; init; }

    public long SizeBytes { get; init; }

    public double Confidence { get; init; }

    public string Evidence { get; init; } = string.Empty;
}
