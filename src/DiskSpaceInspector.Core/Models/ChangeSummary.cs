namespace DiskSpaceInspector.Core.Models;

public sealed class ChangeSummary
{
    public bool IsBaseline { get; init; }

    public int DisplayedCount { get; init; }

    public int TotalCount { get; init; }

    public string Message { get; init; } = string.Empty;
}
