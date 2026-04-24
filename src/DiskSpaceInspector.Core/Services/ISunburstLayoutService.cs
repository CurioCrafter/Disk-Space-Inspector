using DiskSpaceInspector.Core.Models;

namespace DiskSpaceInspector.Core.Services;

public interface ISunburstLayoutService
{
    IReadOnlyList<SunburstSegment> Layout(
        FileSystemNode root,
        IReadOnlyDictionary<long, List<FileSystemNode>> childrenByParent,
        int maxDepth = 4,
        int maxSegments = 320);
}
