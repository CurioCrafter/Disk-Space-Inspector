using DiskSpaceInspector.Core.Models;

namespace DiskSpaceInspector.Core.Services;

public interface IDriveDiscoveryService
{
    IReadOnlyList<VolumeInfo> GetVolumes();
}
