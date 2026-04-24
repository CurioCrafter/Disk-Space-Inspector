namespace DiskSpaceInspector.Core.Models;

public sealed class StorageBreakdownItem
{
    public string Key { get; init; } = string.Empty;

    public string Label { get; init; } = string.Empty;

    public long SizeBytes { get; init; }

    public int Count { get; init; }

    public double Fraction { get; init; }

    public string ColorKey { get; init; } = string.Empty;
}
