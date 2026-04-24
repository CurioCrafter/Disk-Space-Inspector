namespace DiskSpaceInspector.Core.Models;

public sealed class StorageRelationship
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public Guid ScanId { get; init; }

    public long SourceNodeId { get; init; }

    public string SourcePath { get; init; } = string.Empty;

    public string? TargetPath { get; init; }

    public FileSystemEdgeKind Kind { get; init; }

    public string Label { get; init; } = string.Empty;

    public string Owner { get; init; } = string.Empty;

    public RelationshipEvidence Evidence { get; init; } = new();
}
