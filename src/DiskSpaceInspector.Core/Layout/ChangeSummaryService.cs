using DiskSpaceInspector.Core.Models;

namespace DiskSpaceInspector.Core.Layout;

public static class ChangeSummaryService
{
    public static ChangeSummary Summarize(IReadOnlyList<ChangeRecord> changes, int nodeCount)
    {
        if (changes.Count == 0)
        {
            return new ChangeSummary
            {
                Message = "No snapshot changes are recorded yet. Run another scan later to compare."
            };
        }

        var addedCount = changes.Count(change => change.Kind == ChangeKind.Added);
        var looksLikeBaseline = addedCount == changes.Count &&
                                changes.All(change => change.PreviousSizeBytes == 0) &&
                                changes.Count >= Math.Min(25, Math.Max(1, nodeCount / 2));

        if (looksLikeBaseline)
        {
            return new ChangeSummary
            {
                IsBaseline = true,
                TotalCount = changes.Count,
                Message = "This scan is the baseline. Run another scan after using the PC to see new, deleted, grown, shrunk, and moved items."
            };
        }

        var grown = changes.Count(change => change.DeltaBytes > 0);
        var shrunk = changes.Count(change => change.DeltaBytes < 0);
        return new ChangeSummary
        {
            DisplayedCount = changes.Count,
            TotalCount = changes.Count,
            Message = $"{changes.Count:n0} changes detected: {grown:n0} grew or appeared, {shrunk:n0} shrank or disappeared."
        };
    }
}
