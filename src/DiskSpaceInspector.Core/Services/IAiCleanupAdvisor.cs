using DiskSpaceInspector.Core.Models;

namespace DiskSpaceInspector.Core.Services;

public interface IAiCleanupAdvisor
{
    Task<IReadOnlyList<AiCleanupRecommendation>> RecommendAsync(
        AiCleanupAdvisorRequest request,
        AiCleanupAdvisorOptions options,
        CancellationToken cancellationToken = default);
}
