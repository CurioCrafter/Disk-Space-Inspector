using DiskSpaceInspector.Core.Models;

namespace DiskSpaceInspector.App.ViewModels;

public sealed class ConnectionViewModel
{
    public ConnectionViewModel(FileSystemEdge edge, IReadOnlyDictionary<long, FileSystemNode> nodes)
    {
        Kind = edge.Kind.ToString();
        Label = edge.Label;
        Source = nodes.TryGetValue(edge.SourceNodeId, out var source) ? source.FullPath : edge.SourceNodeId.ToString();
        Target = edge.TargetNodeId is { } targetId && nodes.TryGetValue(targetId, out var target)
            ? target.FullPath
            : edge.TargetPath ?? string.Empty;
        Evidence = edge.Evidence;
    }

    public string Kind { get; }

    public string Label { get; }

    public string Source { get; }

    public string Target { get; }

    public string Evidence { get; }
}
