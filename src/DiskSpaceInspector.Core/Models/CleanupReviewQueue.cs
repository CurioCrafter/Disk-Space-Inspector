namespace DiskSpaceInspector.Core.Models;

public sealed class CleanupReviewQueue
{
    public List<CleanupReviewItem> Items { get; init; } = [];

    public long TotalBytes => Items.Sum(item => item.SizeBytes);

    public int TotalFileCount => Items.Sum(item => item.FileCount);
}

public sealed class CleanupReviewItem
{
    public Guid FindingId { get; init; }

    public long NodeId { get; init; }

    public string Path { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string Category { get; init; } = string.Empty;

    public CleanupSafety Safety { get; init; }

    public CleanupActionKind RecommendedAction { get; init; }

    public long SizeBytes { get; init; }

    public int FileCount { get; init; }

    public double Confidence { get; init; }

    public string Evidence { get; init; } = string.Empty;

    public string Explanation { get; init; } = string.Empty;

    public DateTimeOffset AddedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class CleanupReviewResult
{
    public bool Added { get; init; }

    public bool AlreadyStaged { get; init; }

    public string Message { get; init; } = string.Empty;

    public CleanupReviewQueue Queue { get; init; } = new();
}

public sealed class CleanupReviewExport
{
    public string DirectoryPath { get; init; } = string.Empty;

    public string SummaryPath { get; init; } = string.Empty;

    public string DataPath { get; init; } = string.Empty;

    public int RedactedPathCount { get; init; }
}
