namespace DiskSpaceInspector.Core.Models;

public sealed class NodeIdentity
{
    public string StableId { get; init; } = string.Empty;

    public string PathHash { get; init; } = string.Empty;

    public string? ParentStableId { get; init; }

    public string? ParentPathHash { get; init; }

    public string? VolumeSerial { get; init; }

    public string? FileId { get; init; }

    public bool IsPathFallback { get; init; }
}
