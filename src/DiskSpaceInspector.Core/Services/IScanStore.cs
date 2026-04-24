using DiskSpaceInspector.Core.Models;

namespace DiskSpaceInspector.Core.Services;

public interface IScanStore
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task BeginScanAsync(
        ScanSession session,
        VolumeInfo volume,
        CancellationToken cancellationToken = default);

    Task SaveScanBatchAsync(
        ScanBatch batch,
        IEnumerable<CleanupFinding> findings,
        IEnumerable<InsightFinding> insights,
        CancellationToken cancellationToken = default);

    Task CompleteScanAsync(
        ScanCompleted completed,
        CancellationToken cancellationToken = default);

    Task SaveScanAsync(
        ScanResult result,
        IEnumerable<CleanupFinding> findings,
        CancellationToken cancellationToken = default);

    Task<ScanResult?> LoadCurrentScanAsync(CancellationToken cancellationToken = default);

    Task<ScanResult?> LoadLatestScanAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CleanupFinding>> LoadCleanupFindingsAsync(
        Guid scanId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ChangeRecord>> LoadChangeRecordsAsync(
        Guid scanId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StorageRelationship>> LoadRelationshipsAsync(
        Guid scanId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<InsightFinding>> LoadInsightsAsync(
        Guid scanId,
        CancellationToken cancellationToken = default);

    Task<DriveDashboard?> LoadDriveDashboardAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FileSystemNode>> LoadChildrenAsync(
        string? parentStableId,
        NodeQuerySort sort = NodeQuerySort.SizeDescending,
        int skip = 0,
        int take = 250,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FileSystemNode>> SearchNodesAsync(
        string query,
        int skip = 0,
        int take = 250,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FileSystemNode>> LoadTreemapDataAsync(
        string? parentStableId,
        int maxTiles = 240,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FileSystemNode>> LoadSunburstDataAsync(
        string? parentStableId,
        int depth = 4,
        int maxNodes = 500,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StorageBreakdownItem>> LoadTypeBreakdownAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AgeHistogramBucket>> LoadAgeHistogramAsync(
        CancellationToken cancellationToken = default);
}
