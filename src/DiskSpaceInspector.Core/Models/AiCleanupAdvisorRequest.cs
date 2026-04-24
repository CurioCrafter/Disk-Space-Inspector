namespace DiskSpaceInspector.Core.Models;

public sealed class AiCleanupAdvisorRequest
{
    public Guid ScanId { get; init; }

    public IReadOnlyList<CleanupFinding> CleanupFindings { get; init; } = [];

    public IReadOnlyList<InsightFinding> Insights { get; init; } = [];

    public IReadOnlyList<StorageRelationship> Relationships { get; init; } = [];
}
