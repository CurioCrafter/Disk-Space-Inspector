using DiskSpaceInspector.App.Infrastructure;
using DiskSpaceInspector.Core.Models;

namespace DiskSpaceInspector.App.ViewModels;

public sealed class TreemapTileViewModel
{
    public TreemapTileViewModel(TreemapRectangle rectangle)
    {
        NodeId = rectangle.NodeId;
        Label = rectangle.Label;
        Path = rectangle.Path;
        SizeBytes = rectangle.SizeBytes;
        X = rectangle.Bounds.X;
        Y = rectangle.Bounds.Y;
        Width = rectangle.Bounds.Width;
        Height = rectangle.Bounds.Height;
        ColorKey = rectangle.ColorKey;
    }

    public long? NodeId { get; }

    public string Label { get; }

    public string Path { get; }

    public long SizeBytes { get; }

    public string SizeDisplay => ByteFormatter.Format(SizeBytes);

    public double X { get; }

    public double Y { get; }

    public double Width { get; }

    public double Height { get; }

    public string ColorKey { get; }
}
