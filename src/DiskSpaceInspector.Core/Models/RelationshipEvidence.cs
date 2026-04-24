namespace DiskSpaceInspector.Core.Models;

public sealed class RelationshipEvidence
{
    public string Source { get; init; } = string.Empty;

    public string Detail { get; init; } = string.Empty;

    public double Confidence { get; init; }
}
