namespace DiskSpaceInspector.Core.Models;

public enum CleanupActionKind
{
    ClearCache,
    EmptyRecycleBin,
    ReviewDownloads,
    RemoveDuplicateCopy,
    RunSystemCleanup,
    UninstallApp,
    ArchiveOrMove,
    LeaveAlone
}
