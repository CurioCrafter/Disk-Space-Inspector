using System.IO;

namespace DiskSpaceInspector.Core.Models;

public sealed class FileSystemNode
{
    public long Id { get; init; }

    public string StableId { get; set; } = string.Empty;

    public string PathHash { get; set; } = string.Empty;

    public string? ParentStableId { get; set; }

    public string? ParentPathHash { get; set; }

    public long? ParentId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string FullPath { get; set; } = string.Empty;

    public FileSystemNodeKind Kind { get; set; }

    public string? Extension { get; set; }

    public long Length { get; set; }

    public long AllocatedLength { get; set; }

    public long PhysicalLength { get; set; }

    public long TotalLength { get; set; }

    public long TotalPhysicalLength { get; set; }

    public int FileCount { get; set; }

    public int FolderCount { get; set; }

    public int Depth { get; set; }

    public DateTimeOffset? LastModifiedUtc { get; set; }

    public FileAttributes Attributes { get; set; }

    public bool IsReparsePoint { get; set; }

    public string? ReparseTarget { get; set; }

    public string? VolumeSerial { get; set; }

    public string? FileId { get; set; }

    public long? Usn { get; set; }

    public string? ReparseTag { get; set; }

    public int HardLinkCount { get; set; }

    public bool IsHardLinkDuplicate { get; set; }

    public bool IsInaccessiblePlaceholder { get; set; }

    public string Category { get; set; } = "Other";
}
