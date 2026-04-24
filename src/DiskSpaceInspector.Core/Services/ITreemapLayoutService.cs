using DiskSpaceInspector.Core.Models;

namespace DiskSpaceInspector.Core.Services;

public interface ITreemapLayoutService
{
    IReadOnlyList<TreemapRectangle> Layout(
        FileSystemNode parent,
        IReadOnlyList<FileSystemNode> children,
        TreemapBounds bounds,
        int maxTiles = 180);
}
