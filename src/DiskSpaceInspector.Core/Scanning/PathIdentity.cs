using System.Security.Cryptography;
using System.Text;
using DiskSpaceInspector.Core.Models;

namespace DiskSpaceInspector.Core.Scanning;

public static class PathIdentity
{
    public static NodeIdentity Create(
        string fullPath,
        string? volumeSerial,
        string? fileId,
        string? parentStableId,
        string? parentPathHash)
    {
        var pathHash = HashPath(fullPath);
        var hasFileIdentity = !string.IsNullOrWhiteSpace(volumeSerial) && !string.IsNullOrWhiteSpace(fileId);
        return new NodeIdentity
        {
            StableId = hasFileIdentity
                ? $"file:{volumeSerial}:{fileId}"
                : $"path:{pathHash}",
            PathHash = pathHash,
            ParentStableId = parentStableId,
            ParentPathHash = parentPathHash,
            VolumeSerial = volumeSerial,
            FileId = fileId,
            IsPathFallback = !hasFileIdentity
        };
    }

    public static string HashPath(string path)
    {
        var normalized = path.TrimEnd('\\').Replace('/', '\\').ToUpperInvariant();
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes[..12]);
    }
}
