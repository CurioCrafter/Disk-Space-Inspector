using DiskSpaceInspector.Core.Models;

namespace DiskSpaceInspector.Core.Services;

public interface IRelationshipResolver
{
    IReadOnlyList<FileSystemEdge> Resolve(FileSystemNode node);
}
