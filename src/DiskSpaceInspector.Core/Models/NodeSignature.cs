namespace DiskSpaceInspector.Core.Models;

public sealed class NodeSignature
{
    public string StableId { get; init; } = string.Empty;

    public string? LastSeenScanId { get; init; }

    public string PathHash { get; init; } = string.Empty;

    public string FullPath { get; init; } = string.Empty;

    public long Length { get; init; }

    public long AllocatedLength { get; init; }

    public DateTimeOffset? LastModifiedUtc { get; init; }

    public string Attributes { get; init; } = string.Empty;

    public string? ReparseTarget { get; init; }

    public long? Usn { get; init; }
}
