using DiskSpaceInspector.Core.Models;

namespace DiskSpaceInspector.Core.Services;

public interface ICleanupClassifier
{
    CleanupFinding? Classify(FileSystemNode node);
}
