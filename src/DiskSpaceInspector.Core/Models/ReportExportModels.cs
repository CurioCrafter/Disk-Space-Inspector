namespace DiskSpaceInspector.Core.Models;

public enum PathPrivacyMode
{
    RedactedUserProfile,
    Raw
}

public sealed class ReportExportOptions
{
    public string OutputDirectory { get; init; } = string.Empty;

    public PathPrivacyMode PathPrivacyMode { get; init; } = PathPrivacyMode.RedactedUserProfile;

    public int MaxNodes { get; init; } = 500;

    public int MaxFindings { get; init; } = 250;

    public bool IncludeInsights { get; init; } = true;

    public bool IncludeRelationships { get; init; } = true;
}

public sealed class ReportBundle
{
    public string DirectoryPath { get; init; } = string.Empty;

    public string SummaryPath { get; init; } = string.Empty;

    public string DataPath { get; init; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; init; }

    public int RedactedPathCount { get; init; }

    public int FileCount { get; init; }
}
