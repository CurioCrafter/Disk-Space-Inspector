using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using DiskSpaceInspector.Core.Models;
using DiskSpaceInspector.Core.Services;

namespace DiskSpaceInspector.Core.Windows;

public sealed class WindowsDriveDiscoveryService : IDriveDiscoveryService
{
    public IReadOnlyList<VolumeInfo> GetVolumes()
    {
        var volumes = new List<VolumeInfo>();
        foreach (var drive in DriveInfo.GetDrives().OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
        {
            volumes.Add(ReadDrive(drive));
        }

        return volumes;
    }

    private static VolumeInfo ReadDrive(DriveInfo drive)
    {
        var total = 0L;
        var free = 0L;
        var isReady = false;
        string? label = null;
        string? format = null;
        string? serial = null;

        try
        {
            isReady = drive.IsReady;
            if (isReady)
            {
                total = drive.TotalSize;
                free = drive.AvailableFreeSpace;
                label = drive.VolumeLabel;
                format = drive.DriveFormat;
                serial = TryGetVolumeSerial(drive.RootDirectory.FullName);
            }
        }
        catch
        {
            isReady = false;
        }

        return new VolumeInfo
        {
            Name = drive.Name.TrimEnd('\\'),
            RootPath = drive.RootDirectory.FullName,
            Label = label,
            FileSystem = format,
            VolumeSerial = serial,
            DriveType = drive.DriveType.ToString(),
            IsReady = isReady,
            TotalBytes = total,
            FreeBytes = free
        };
    }

    private static string? TryGetVolumeSerial(string rootPath)
    {
        var volumeName = new StringBuilder(261);
        var fileSystemName = new StringBuilder(261);
        return GetVolumeInformationW(
            rootPath,
            volumeName,
            volumeName.Capacity,
            out var serial,
            out _,
            out _,
            fileSystemName,
            fileSystemName.Capacity)
            ? serial.ToString("X8")
            : null;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetVolumeInformationW(
        string lpRootPathName,
        StringBuilder lpVolumeNameBuffer,
        int nVolumeNameSize,
        out uint lpVolumeSerialNumber,
        out uint lpMaximumComponentLength,
        out uint lpFileSystemFlags,
        StringBuilder lpFileSystemNameBuffer,
        int nFileSystemNameSize);
}
