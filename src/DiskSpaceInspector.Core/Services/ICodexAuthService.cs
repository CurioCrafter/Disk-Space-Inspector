using DiskSpaceInspector.Core.Models;

namespace DiskSpaceInspector.Core.Services;

public interface ICodexAuthService
{
    Task<CodexAuthStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    Task StartLoginAsync(CancellationToken cancellationToken = default);
}
