using DiskSpaceInspector.Core.Models;

namespace DiskSpaceInspector.Core.Services;

public interface IStorageAnalyticsService
{
    StorageAnalyticsSnapshot BuildSnapshot(
        ScanResult scan,
        IReadOnlyList<CleanupFinding> findings,
        IReadOnlyList<StorageRelationship> relationships,
        IReadOnlyList<ChangeRecord> changes,
        IReadOnlyList<VolumeInfo> volumes);
}
