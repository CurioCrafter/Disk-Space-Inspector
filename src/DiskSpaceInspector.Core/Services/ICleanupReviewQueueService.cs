using DiskSpaceInspector.Core.Models;

namespace DiskSpaceInspector.Core.Services;

public interface ICleanupReviewQueueService
{
    CleanupReviewResult TryStage(CleanupReviewQueue queue, CleanupFinding finding);

    CleanupReviewQueue Remove(CleanupReviewQueue queue, Guid findingId);

    CleanupReviewQueue Clear(CleanupReviewQueue queue);

    Task<CleanupReviewExport> ExportAsync(
        CleanupReviewQueue queue,
        string outputDirectory,
        PathPrivacyMode privacyMode,
        CancellationToken cancellationToken = default);
}
