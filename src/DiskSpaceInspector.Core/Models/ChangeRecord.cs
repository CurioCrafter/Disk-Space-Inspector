namespace DiskSpaceInspector.Core.Models;

public sealed class ChangeRecord
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public Guid ScanId { get; init; }

    public string StableId { get; init; } = string.Empty;

    public string Path { get; init; } = string.Empty;

    public string? PreviousPath { get; init; }

    public ChangeKind Kind { get; init; }

    public long PreviousSizeBytes { get; init; }

    public long CurrentSizeBytes { get; init; }

    public long DeltaBytes => CurrentSizeBytes - PreviousSizeBytes;

    public DateTimeOffset DetectedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public string Reason { get; init; } = string.Empty;
}
