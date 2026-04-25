using DiskSpaceInspector.Core.Models;

namespace DiskSpaceInspector.Core.Services;

public interface IFirstRunStateStore
{
    Task<FirstRunState> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(FirstRunState state, CancellationToken cancellationToken = default);
}
