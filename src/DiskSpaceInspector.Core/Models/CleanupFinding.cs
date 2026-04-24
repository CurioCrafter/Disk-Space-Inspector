namespace DiskSpaceInspector.Core.Models;

public sealed class CleanupFinding
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public long NodeId { get; init; }

    public string Path { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string Category { get; init; } = string.Empty;

    public CleanupSafety Safety { get; init; }

    public CleanupActionKind RecommendedAction { get; init; }

    public long SizeBytes { get; init; }

    public int FileCount { get; init; }

    public DateTimeOffset? LastModifiedUtc { get; init; }

    public double Confidence { get; init; }

    public string Explanation { get; init; } = string.Empty;

    public string MatchedRule { get; init; } = string.Empty;

    public string? AppOrSource { get; init; }

    public Dictionary<string, string> Evidence { get; init; } = [];
}
