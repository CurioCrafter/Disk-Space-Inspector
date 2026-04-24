using DiskSpaceInspector.Core.Models;
using DiskSpaceInspector.Core.Services;

namespace DiskSpaceInspector.Core.Cleanup;

public sealed class CleanupClassifier : ICleanupClassifier
{
    private static readonly HashSet<string> InstallerExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".iso", ".msi", ".msix", ".appx", ".exe", ".zip", ".7z", ".rar"
    };

    private static readonly HashSet<string> LargeReviewExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mov", ".mkv", ".avi", ".vhd", ".vhdx", ".vmdk", ".bak", ".dump", ".sql"
    };

    private static readonly HashSet<string> BrowserStateFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "Login Data", "Cookies", "History", "Local State", "Web Data"
    };

    public CleanupFinding? Classify(FileSystemNode node)
    {
        if (node.TotalPhysicalLength <= 0 && node.PhysicalLength <= 0)
        {
            return null;
        }

        var path = Normalize(node.FullPath);
        var name = node.Name;
        var size = Math.Max(node.TotalPhysicalLength, node.PhysicalLength);

        if (IsBlockedSystemPath(path, name))
        {
            return Finding(
                node,
                "System managed storage",
                CleanupSafety.Blocked,
                CleanupActionKind.LeaveAlone,
                1.0,
                "This path is managed by Windows or contains active application state. Disk Space Inspector will not plan direct deletion for it.",
                "blocked-system-path",
                "Windows");
        }

        if (IsWindowsCleanupPath(path))
        {
            return Finding(
                node,
                "Windows cleanup",
                CleanupSafety.UseSystemCleanup,
                CleanupActionKind.RunSystemCleanup,
                0.92,
                "This space is controlled by Windows cleanup mechanisms. Use the supported system cleanup route instead of deleting files directly.",
                "windows-cleanup-path",
                "Windows");
        }

        if (path.Contains(@"\$recycle.bin\", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(@"\$recycle.bin", StringComparison.OrdinalIgnoreCase))
        {
            return Finding(
                node,
                "Recycle Bin",
                CleanupSafety.Safe,
                CleanupActionKind.EmptyRecycleBin,
                0.95,
                "Recycle Bin contents are already staged for removal and can be cleared through the shell.",
                "recycle-bin",
                "Windows Shell");
        }

        if (IsTempPath(path))
        {
            return Finding(
                node,
                "Temporary files",
                CleanupSafety.Safe,
                CleanupActionKind.ClearCache,
                0.88,
                "This folder matches a temporary-file location. Apps normally regenerate these files when needed.",
                "temp-path");
        }

        if (IsBrowserState(path, name))
        {
            return Finding(
                node,
                "Protected app state",
                CleanupSafety.Blocked,
                CleanupActionKind.LeaveAlone,
                0.98,
                "This looks like browser or app state, not disposable cache. It may contain sessions, cookies, credentials, or history.",
                "protected-browser-state",
                "Browser profile");
        }

        if (IsCachePath(path, name))
        {
            return Finding(
                node,
                "Application cache",
                CleanupSafety.Safe,
                CleanupActionKind.ClearCache,
                0.78,
                "This matches a known cache folder. Clearing it may free space, but the owning app may rebuild it later.",
                "cache-path",
                GuessAppSource(path));
        }

        if (IsPackageCache(path))
        {
            return Finding(
                node,
                "Package cache",
                CleanupSafety.UseSystemCleanup,
                CleanupActionKind.RunSystemCleanup,
                0.82,
                "This appears to be a package-manager cache. Prefer the package manager cleanup command over manual deletion.",
                "package-cache",
                GuessAppSource(path));
        }

        if (IsDownloadsPath(path))
        {
            return Finding(
                node,
                "Downloads review",
                CleanupSafety.Review,
                CleanupActionKind.ReviewDownloads,
                0.7,
                "Downloaded files are user-owned. Review age, type, and whether the file is still needed before removing it.",
                "downloads-path");
        }

        if (IsDeveloperArtifact(path, name))
        {
            return Finding(
                node,
                "Developer artifact",
                CleanupSafety.Review,
                CleanupActionKind.ClearCache,
                0.75,
                "This looks like generated project output or dependency material. It is usually rebuildable, but confirm the project context first.",
                "developer-artifact",
                "Developer toolchain");
        }

        if (node.Kind == FileSystemNodeKind.File &&
            (InstallerExtensions.Contains(node.Extension ?? string.Empty) || LargeReviewExtensions.Contains(node.Extension ?? string.Empty)) &&
            size >= 50 * 1024 * 1024)
        {
            return Finding(
                node,
                "Large file review",
                CleanupSafety.Review,
                CleanupActionKind.ArchiveOrMove,
                0.62,
                "This is a large installer, archive, media file, backup, database dump, or disk image. Review before deleting or moving it.",
                "large-review-extension");
        }

        if (node.HardLinkCount > 1)
        {
            return Finding(
                node,
                "Hardlinked file",
                CleanupSafety.Review,
                CleanupActionKind.LeaveAlone,
                0.8,
                "This file has multiple hardlinks. Removing one path may not reclaim disk space until every link is removed.",
                "hardlink");
        }

        return null;
    }

    private static CleanupFinding Finding(
        FileSystemNode node,
        string category,
        CleanupSafety safety,
        CleanupActionKind action,
        double confidence,
        string explanation,
        string matchedRule,
        string? appOrSource = null)
    {
        var size = Math.Max(node.TotalPhysicalLength, node.PhysicalLength);
        return new CleanupFinding
        {
            NodeId = node.Id,
            Path = node.FullPath,
            DisplayName = string.IsNullOrWhiteSpace(node.Name) ? node.FullPath : node.Name,
            Category = category,
            Safety = safety,
            RecommendedAction = action,
            SizeBytes = size,
            FileCount = Math.Max(node.FileCount, node.Kind == FileSystemNodeKind.File ? 1 : 0),
            LastModifiedUtc = node.LastModifiedUtc,
            Confidence = confidence,
            Explanation = explanation,
            MatchedRule = matchedRule,
            AppOrSource = appOrSource,
            Evidence =
            {
                ["path"] = node.FullPath,
                ["kind"] = node.Kind.ToString(),
                ["sizeBytes"] = size.ToString(),
                ["matchedRule"] = matchedRule,
                ["safety"] = safety.ToString()
            }
        };
    }

    private static string Normalize(string path)
    {
        return path.TrimEnd('\\').Replace('/', '\\');
    }

    private static bool IsBlockedSystemPath(string path, string name)
    {
        return path.Contains(@"\windows\installer", StringComparison.OrdinalIgnoreCase) ||
               path.Contains(@"\windows\winsxs", StringComparison.OrdinalIgnoreCase) ||
               path.Contains(@"\windows\system32", StringComparison.OrdinalIgnoreCase) ||
               path.Contains(@"\windows\system32\driverstore", StringComparison.OrdinalIgnoreCase) ||
               path.Contains(@"\windows\system volume information", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(@"\pagefile.sys", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(@"\swapfile.sys", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(@"\hiberfil.sys", StringComparison.OrdinalIgnoreCase) ||
               path.Contains(@"\microsoft\credentials", StringComparison.OrdinalIgnoreCase) ||
               BrowserStateFiles.Contains(name);
    }

    private static bool IsWindowsCleanupPath(string path)
    {
        return path.Contains(@"\windows\softwaredistribution\download", StringComparison.OrdinalIgnoreCase) ||
               path.Contains(@"\windows\logs\cbs", StringComparison.OrdinalIgnoreCase) ||
               path.Contains(@"\windows\logs\dism", StringComparison.OrdinalIgnoreCase) ||
               path.Contains(@"\programdata\microsoft\windows\wer", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTempPath(string path)
    {
        var localTemp = Normalize(Path.GetTempPath());
        return path.StartsWith(localTemp, StringComparison.OrdinalIgnoreCase) ||
               path.Contains(@"\appdata\local\temp", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(@"\windows\temp", StringComparison.OrdinalIgnoreCase) ||
               path.Contains(@"\windows\temp\", StringComparison.OrdinalIgnoreCase) ||
               path.Contains(@"\appdata\local\crashdumps", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCachePath(string path, string name)
    {
        return name.Equals("Cache", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("Code Cache", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("GPUCache", StringComparison.OrdinalIgnoreCase) ||
               path.Contains(@"\service worker\cachestorage", StringComparison.OrdinalIgnoreCase) ||
               path.Contains(@"\inetcache", StringComparison.OrdinalIgnoreCase) ||
               path.Contains(@"\appdata\local\microsoft\windows\explorer", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPackageCache(string path)
    {
        return path.Contains(@"\.nuget\packages", StringComparison.OrdinalIgnoreCase) ||
               path.Contains(@"\npm-cache", StringComparison.OrdinalIgnoreCase) ||
               path.Contains(@"\pip\cache", StringComparison.OrdinalIgnoreCase) ||
               path.Contains(@"\.gradle\caches", StringComparison.OrdinalIgnoreCase) ||
               path.Contains(@"\yarn\cache", StringComparison.OrdinalIgnoreCase) ||
               path.Contains(@"\pnpm\store", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDownloadsPath(string path)
    {
        return path.Contains(@"\downloads\", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(@"\downloads", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDeveloperArtifact(string path, string name)
    {
        return name.Equals("node_modules", StringComparison.OrdinalIgnoreCase) ||
               name.Equals(".venv", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("build", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("dist", StringComparison.OrdinalIgnoreCase) ||
               name.Equals(".next", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("target", StringComparison.OrdinalIgnoreCase) ||
               path.Contains(@"\obj\", StringComparison.OrdinalIgnoreCase) ||
               path.Contains(@"\bin\debug", StringComparison.OrdinalIgnoreCase) ||
               path.Contains(@"\bin\release", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBrowserState(string path, string name)
    {
        return BrowserStateFiles.Contains(name) &&
               (path.Contains(@"\chrome\", StringComparison.OrdinalIgnoreCase) ||
                path.Contains(@"\edge\", StringComparison.OrdinalIgnoreCase) ||
                path.Contains(@"\firefox\", StringComparison.OrdinalIgnoreCase) ||
                path.Contains(@"\brave", StringComparison.OrdinalIgnoreCase));
    }

    private static string? GuessAppSource(string path)
    {
        if (path.Contains(@"\discord\", StringComparison.OrdinalIgnoreCase)) return "Discord";
        if (path.Contains(@"\slack\", StringComparison.OrdinalIgnoreCase)) return "Slack";
        if (path.Contains(@"\teams\", StringComparison.OrdinalIgnoreCase)) return "Teams";
        if (path.Contains(@"\code\", StringComparison.OrdinalIgnoreCase)) return "VS Code";
        if (path.Contains(@"\spotify\", StringComparison.OrdinalIgnoreCase)) return "Spotify";
        if (path.Contains(@"\docker\", StringComparison.OrdinalIgnoreCase)) return "Docker";
        if (path.Contains(@"\.nuget\", StringComparison.OrdinalIgnoreCase)) return "NuGet";
        if (path.Contains(@"\npm", StringComparison.OrdinalIgnoreCase)) return "npm";
        if (path.Contains(@"\pip\", StringComparison.OrdinalIgnoreCase)) return "pip";
        return null;
    }
}
