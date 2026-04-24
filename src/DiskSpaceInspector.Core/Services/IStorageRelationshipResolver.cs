using DiskSpaceInspector.Core.Models;

namespace DiskSpaceInspector.Core.Services;

public interface IStorageRelationshipResolver
{
    IReadOnlyList<StorageRelationship> ResolveRelationships(FileSystemNode node, Guid scanId);
}
