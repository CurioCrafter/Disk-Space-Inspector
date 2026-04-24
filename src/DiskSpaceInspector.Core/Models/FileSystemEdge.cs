namespace DiskSpaceInspector.Core.Models;

public sealed class FileSystemEdge
{
    public long Id { get; init; }

    public long SourceNodeId { get; init; }

    public long? TargetNodeId { get; init; }

    public string? TargetPath { get; init; }

    public FileSystemEdgeKind Kind { get; init; }

    public string Label { get; init; } = string.Empty;

    public string Evidence { get; init; } = string.Empty;
}
