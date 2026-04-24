namespace DiskSpaceInspector.Core.Models;

public sealed class AiCleanupRecommendation
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public Guid SourceFindingId { get; init; }

    public long NodeId { get; init; }

    public string Path { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string Category { get; init; } = string.Empty;

    public CleanupSafety Safety { get; init; }

    public CleanupActionKind RecommendedAction { get; init; }

    public string AiVerb { get; init; } = string.Empty;

    public long SizeBytes { get; init; }

    public double Confidence { get; init; }

    public string Reasoning { get; init; } = string.Empty;

    public string Evidence { get; init; } = string.Empty;

    public string Guardrail { get; init; } = string.Empty;

    public bool CanStage { get; init; }

    public string Model { get; init; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
