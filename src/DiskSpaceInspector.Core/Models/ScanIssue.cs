namespace DiskSpaceInspector.Core.Models;

public sealed class ScanIssue
{
    public long Id { get; init; }

    public long? NodeId { get; init; }

    public string Path { get; init; } = string.Empty;

    public string Operation { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public int? NativeErrorCode { get; init; }

    public bool ElevationMayHelp { get; init; }

    public DateTimeOffset OccurredAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
