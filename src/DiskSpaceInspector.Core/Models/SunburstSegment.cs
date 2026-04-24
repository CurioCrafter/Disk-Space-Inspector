namespace DiskSpaceInspector.Core.Models;

public sealed class SunburstSegment
{
    public long? NodeId { get; init; }

    public string Label { get; init; } = string.Empty;

    public string Path { get; init; } = string.Empty;

    public long SizeBytes { get; init; }

    public double StartAngle { get; init; }

    public double SweepAngle { get; init; }

    public double InnerRadius { get; init; }

    public double OuterRadius { get; init; }

    public int Depth { get; init; }

    public string ColorKey { get; init; } = string.Empty;
}
