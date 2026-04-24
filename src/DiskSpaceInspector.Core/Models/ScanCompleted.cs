namespace DiskSpaceInspector.Core.Models;

public sealed class ScanCompleted
{
    public ScanSession Session { get; init; } = new();

    public VolumeInfo Volume { get; init; } = new();

    public ScanMetrics FinalMetrics { get; init; } = new();

    public int BatchCount { get; init; }
}
