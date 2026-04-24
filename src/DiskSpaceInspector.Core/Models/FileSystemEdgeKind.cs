namespace DiskSpaceInspector.Core.Models;

public enum FileSystemEdgeKind
{
    SymlinkTarget,
    JunctionTarget,
    HardlinkSibling,
    AppOwnership,
    CacheOwnership,
    Duplicate,
    PackageArtifact
}
