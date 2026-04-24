namespace DiskSpaceInspector.Core.Models;

public sealed class CleanupActionPlan
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public List<CleanupFinding> Findings { get; init; } = [];

    public long EstimatedReclaimableBytes => Findings.Sum(f => f.SizeBytes);

    public int BlockedCount { get; init; }

    public int SystemCleanupCount { get; init; }
}
