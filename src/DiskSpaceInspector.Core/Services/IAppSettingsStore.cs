using DiskSpaceInspector.Core.Models;

namespace DiskSpaceInspector.Core.Services;

public interface IAppSettingsStore
{
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
}
