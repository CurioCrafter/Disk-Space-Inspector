namespace DiskSpaceInspector.Core.Models;

public sealed class TreemapRectangle
{
    public long? NodeId { get; init; }

    public string Label { get; init; } = string.Empty;

    public string Path { get; init; } = string.Empty;

    public long SizeBytes { get; init; }

    public TreemapBounds Bounds { get; init; }

    public string ColorKey { get; init; } = string.Empty;
}
