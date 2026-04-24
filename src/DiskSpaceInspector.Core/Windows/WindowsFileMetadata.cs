using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DiskSpaceInspector.Core.Windows;

public static class WindowsFileMetadata
{
    private const uint FileReadAttributes = 0x80;
    private const uint FileShareRead = 0x1;
    private const uint FileShareWrite = 0x2;
    private const uint FileShareDelete = 0x4;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint FileFlagOpenReparsePoint = 0x00200000;

    public static FileIdentity? TryGetIdentity(string path, bool isDirectory)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var flags = FileFlagOpenReparsePoint | (isDirectory ? FileFlagBackupSemantics : 0);
        using var handle = CreateFileW(
            ToExtendedPath(path),
            FileReadAttributes,
            FileShareRead | FileShareWrite | FileShareDelete,
            IntPtr.Zero,
            OpenExisting,
            flags,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            return null;
        }

        if (!GetFileInformationByHandle(handle, out var info))
        {
            return null;
        }

        var fileId = $"{info.FileIndexHigh:X8}{info.FileIndexLow:X8}";
        return new FileIdentity(info.VolumeSerialNumber.ToString("X8"), fileId, (int)info.NumberOfLinks);
    }

    public static long GetAllocatedSize(string path, long fallbackLength)
    {
        if (!OperatingSystem.IsWindows())
        {
            return fallbackLength;
        }

        var low = GetCompressedFileSizeW(ToExtendedPath(path), out var high);
        var error = Marshal.GetLastWin32Error();
        if (low == uint.MaxValue && error != 0)
        {
            return fallbackLength;
        }

        return ((long)high << 32) + low;
    }

    private static string ToExtendedPath(string path)
    {
        if (!OperatingSystem.IsWindows() ||
            path.StartsWith(@"\\?\", StringComparison.Ordinal) ||
            path.StartsWith(@"\\.\", StringComparison.Ordinal))
        {
            return path;
        }

        if (path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return @"\\?\UNC\" + path[2..];
        }

        return @"\\?\" + path;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandle(
        SafeFileHandle hFile,
        out ByHandleFileInformation lpFileInformation);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetCompressedFileSizeW(string lpFileName, out uint lpFileSizeHigh);

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }
}
