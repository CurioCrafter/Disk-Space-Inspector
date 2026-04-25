namespace DiskSpaceInspector.Core.Models;

public sealed class FirstRunState
{
    public bool HasOpenedApp { get; set; }

    public bool HasLoadedDemo { get; set; }

    public bool HasLoadedScan { get; set; }

    public DateTimeOffset? FirstOpenedAtUtc { get; set; }

    public DateTimeOffset? LastOpenedAtUtc { get; set; }

    public DateTimeOffset? LastDemoLoadedAtUtc { get; set; }

    public DateTimeOffset? LastScanLoadedAtUtc { get; set; }
}
