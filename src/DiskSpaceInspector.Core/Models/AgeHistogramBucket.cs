namespace DiskSpaceInspector.Core.Models;

public sealed class AgeHistogramBucket
{
    public string Label { get; init; } = string.Empty;

    public long SizeBytes { get; init; }

    public int Count { get; init; }

    public double Fraction { get; init; }
}
