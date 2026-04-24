namespace DiskSpaceInspector.Core.Models;

public sealed class ScanResult
{
    public ScanSession Session { get; init; } = new();

    public VolumeInfo Volume { get; init; } = new();

    public List<FileSystemNode> Nodes { get; init; } = [];

    public List<FileSystemEdge> Edges { get; init; } = [];

    public List<ScanIssue> Issues { get; init; } = [];
}
