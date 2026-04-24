using DiskSpaceInspector.App.Infrastructure;
using DiskSpaceInspector.Core.Models;

namespace DiskSpaceInspector.App.ViewModels;

public sealed class SunburstSegmentViewModel
{
    public SunburstSegmentViewModel(SunburstSegment segment)
    {
        Model = segment;
    }

    public SunburstSegment Model { get; }

    public long? NodeId => Model.NodeId;

    public string Label => Model.Label;

    public string Path => Model.Path;

    public long SizeBytes => Model.SizeBytes;

    public string SizeDisplay => ByteFormatter.Format(Model.SizeBytes);

    public double StartAngle => Model.StartAngle;

    public double SweepAngle => Model.SweepAngle;

    public double InnerRadius => Model.InnerRadius;

    public double OuterRadius => Model.OuterRadius;

    public int Depth => Model.Depth;

    public string ColorKey => Model.ColorKey;
}
