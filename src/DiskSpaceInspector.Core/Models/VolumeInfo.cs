namespace DiskSpaceInspector.Core.Models;

public sealed class VolumeInfo
{
    public string Name { get; init; } = string.Empty;

    public string RootPath { get; init; } = string.Empty;

    public string? FileSystem { get; init; }

    public string? Label { get; init; }

    public string? VolumeSerial { get; init; }

    public string DriveType { get; init; } = string.Empty;

    public bool IsReady { get; init; }

    public long TotalBytes { get; init; }

    public long FreeBytes { get; init; }

    public string DisplayName => string.IsNullOrWhiteSpace(Label)
        ? $"{Name} ({RootPath})"
        : $"{Label} ({RootPath})";
}
