using DiskSpaceInspector.Core.Models;

namespace DiskSpaceInspector.Core.Services;

public interface IStorageBreakdownService
{
    IReadOnlyList<StorageBreakdownItem> BuildTypeBreakdown(IEnumerable<FileSystemNode> nodes, int maxItems = 12);

    IReadOnlyList<AgeHistogramBucket> BuildAgeHistogram(IEnumerable<FileSystemNode> nodes);

    IReadOnlyList<StorageBreakdownItem> BuildCleanupPotential(IEnumerable<CleanupFinding> findings);
}
