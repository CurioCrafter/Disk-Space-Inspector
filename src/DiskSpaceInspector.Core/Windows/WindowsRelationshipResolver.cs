using DiskSpaceInspector.Core.Models;
using DiskSpaceInspector.Core.Services;

namespace DiskSpaceInspector.Core.Windows;

public sealed class WindowsRelationshipResolver : IRelationshipResolver, IStorageRelationshipResolver
{
    private readonly Lazy<IReadOnlyList<InstalledApplication>> _installedApps = new(LoadInstalledApps);

    public IReadOnlyList<FileSystemEdge> Resolve(FileSystemNode node)
    {
        var edges = new List<FileSystemEdge>();

        if (node.IsReparsePoint && !string.IsNullOrWhiteSpace(node.ReparseTarget))
        {
            edges.Add(new FileSystemEdge
            {
                SourceNodeId = node.Id,
                TargetPath = node.ReparseTarget,
                Kind = node.Kind == FileSystemNodeKind.ReparsePoint
                    ? FileSystemEdgeKind.JunctionTarget
                    : FileSystemEdgeKind.SymlinkTarget,
                Label = "points to",
                Evidence = node.ReparseTarget
            });
        }

        var source = GuessSource(node.FullPath);
        if (source is not null)
        {
            edges.Add(new FileSystemEdge
            {
                SourceNodeId = node.Id,
                Kind = FileSystemEdgeKind.CacheOwnership,
                Label = $"owned by {source}",
                Evidence = source
            });
        }

        return edges;
    }

    public IReadOnlyList<StorageRelationship> ResolveRelationships(FileSystemNode node, Guid scanId)
    {
        var relationships = new List<StorageRelationship>();

        if (node.IsReparsePoint && !string.IsNullOrWhiteSpace(node.ReparseTarget))
        {
            relationships.Add(Relationship(
                scanId,
                node,
                FileSystemEdgeKind.JunctionTarget,
                "linked to",
                "Filesystem",
                node.ReparseTarget,
                "Reparse point metadata",
                node.ReparseTarget,
                0.95));
        }

        var installedApp = MatchInstalledApplication(node.FullPath);
        if (installedApp is not null)
        {
            relationships.Add(Relationship(
                scanId,
                node,
                FileSystemEdgeKind.AppOwnership,
                "installed by",
                installedApp.DisplayName,
                installedApp.InstallLocation,
                "Registry uninstall entry",
                installedApp.RegistryKey,
                0.9));
        }

        var appDataOwner = GuessAppDataOwner(node.FullPath);
        if (appDataOwner is not null)
        {
            relationships.Add(Relationship(
                scanId,
                node,
                FileSystemEdgeKind.CacheOwnership,
                "owned by",
                appDataOwner,
                null,
                "AppData or ProgramData path",
                node.FullPath,
                0.72));
        }

        var devRelationship = GuessDeveloperRelationship(node);
        if (devRelationship is not null)
        {
            relationships.Add(Relationship(
                scanId,
                node,
                FileSystemEdgeKind.PackageArtifact,
                devRelationship.Value.Label,
                devRelationship.Value.Owner,
                null,
                devRelationship.Value.EvidenceSource,
                devRelationship.Value.EvidenceDetail,
                devRelationship.Value.Confidence));
        }

        var cloudProvider = GuessCloudProvider(node.FullPath);
        if (cloudProvider is not null)
        {
            relationships.Add(Relationship(
                scanId,
                node,
                FileSystemEdgeKind.CacheOwnership,
                "synced by",
                cloudProvider,
                null,
                "Cloud sync folder path",
                node.FullPath,
                0.8));
        }

        var systemOwner = GuessSystemOwner(node.FullPath);
        if (systemOwner is not null)
        {
            relationships.Add(Relationship(
                scanId,
                node,
                FileSystemEdgeKind.AppOwnership,
                "managed by",
                systemOwner,
                null,
                "Known Windows storage root",
                node.FullPath,
                0.86));
        }

        return relationships;
    }

    private static string? GuessSource(string path)
    {
        if (!path.Contains(@"\appdata\", StringComparison.OrdinalIgnoreCase) &&
            !path.Contains(@"\programdata\", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (path.Contains(@"\discord\", StringComparison.OrdinalIgnoreCase)) return "Discord";
        if (path.Contains(@"\slack\", StringComparison.OrdinalIgnoreCase)) return "Slack";
        if (path.Contains(@"\teams\", StringComparison.OrdinalIgnoreCase)) return "Teams";
        if (path.Contains(@"\spotify\", StringComparison.OrdinalIgnoreCase)) return "Spotify";
        if (path.Contains(@"\code\", StringComparison.OrdinalIgnoreCase)) return "VS Code";
        if (path.Contains(@"\docker\", StringComparison.OrdinalIgnoreCase)) return "Docker";
        return null;
    }

    private StorageRelationship Relationship(
        Guid scanId,
        FileSystemNode node,
        FileSystemEdgeKind kind,
        string label,
        string owner,
        string? targetPath,
        string evidenceSource,
        string evidenceDetail,
        double confidence)
    {
        return new StorageRelationship
        {
            ScanId = scanId,
            SourceNodeId = node.Id,
            SourcePath = node.FullPath,
            TargetPath = targetPath,
            Kind = kind,
            Label = label,
            Owner = owner,
            Evidence = new RelationshipEvidence
            {
                Source = evidenceSource,
                Detail = evidenceDetail,
                Confidence = confidence
            }
        };
    }

    private InstalledApplication? MatchInstalledApplication(string path)
    {
        return _installedApps.Value
            .Where(app => !string.IsNullOrWhiteSpace(app.InstallLocation))
            .Where(app => path.StartsWith(app.InstallLocation!, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(app => app.InstallLocation!.Length)
            .FirstOrDefault();
    }

    private static string? GuessAppDataOwner(string path)
    {
        var markers = new[] { @"\AppData\Local\", @"\AppData\Roaming\", @"\ProgramData\" };
        foreach (var marker in markers)
        {
            var index = path.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                continue;
            }

            var remainder = path[(index + marker.Length)..].Trim('\\');
            var owner = remainder.Split('\\', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(owner))
            {
                return owner;
            }
        }

        return null;
    }

    private static (string Label, string Owner, string EvidenceSource, string EvidenceDetail, double Confidence)? GuessDeveloperRelationship(FileSystemNode node)
    {
        var name = node.Name;
        if (name.Equals("node_modules", StringComparison.OrdinalIgnoreCase))
        {
            return ("generated by", "npm/yarn/pnpm project", "Folder name and package ecosystem convention", "node_modules", 0.9);
        }

        if (name.Equals(".venv", StringComparison.OrdinalIgnoreCase) || name.Equals("venv", StringComparison.OrdinalIgnoreCase))
        {
            return ("generated by", "Python virtual environment", "Folder name convention", name, 0.88);
        }

        if (name.Equals("target", StringComparison.OrdinalIgnoreCase) && File.Exists(Path.Combine(Path.GetDirectoryName(node.FullPath) ?? "", "Cargo.toml")))
        {
            return ("generated by", "Rust build output", "Cargo.toml next to target directory", node.FullPath, 0.92);
        }

        if ((name.Equals("bin", StringComparison.OrdinalIgnoreCase) || name.Equals("obj", StringComparison.OrdinalIgnoreCase)) &&
            HasSiblingProjectFile(node.FullPath, "*.csproj"))
        {
            return ("generated by", ".NET build output", "bin/obj folder convention", node.FullPath, 0.7);
        }

        if (name.Equals(".next", StringComparison.OrdinalIgnoreCase) || name.Equals("dist", StringComparison.OrdinalIgnoreCase) || name.Equals("build", StringComparison.OrdinalIgnoreCase))
        {
            return ("generated by", "project build output", "Common generated output folder", name, 0.7);
        }

        if (node.FullPath.Contains(@"\Docker\", StringComparison.OrdinalIgnoreCase))
        {
            return ("owned by", "Docker", "Docker storage path", node.FullPath, 0.82);
        }

        if (node.FullPath.Contains(@"\wsl\", StringComparison.OrdinalIgnoreCase) || node.Name.EndsWith(".vhdx", StringComparison.OrdinalIgnoreCase))
        {
            return ("owned by", "WSL or virtual disk", "WSL/virtual disk path or extension", node.FullPath, 0.7);
        }

        return null;
    }

    private static bool HasSiblingProjectFile(string path, string pattern)
    {
        try
        {
            var parent = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(parent))
            {
                return false;
            }

            var projectRoot = Path.GetFullPath(Path.Combine(parent, ".."));
            return Directory.Exists(projectRoot) && Directory.EnumerateFiles(projectRoot, pattern).Any();
        }
        catch
        {
            return false;
        }
    }

    private static string? GuessCloudProvider(string path)
    {
        if (path.Contains(@"\OneDrive", StringComparison.OrdinalIgnoreCase)) return "OneDrive";
        if (path.Contains(@"\Dropbox", StringComparison.OrdinalIgnoreCase)) return "Dropbox";
        if (path.Contains(@"\Google Drive", StringComparison.OrdinalIgnoreCase)) return "Google Drive";
        return null;
    }

    private static string? GuessSystemOwner(string path)
    {
        if (path.Contains(@"\Windows\SoftwareDistribution\Download", StringComparison.OrdinalIgnoreCase)) return "Windows Update";
        if (path.Contains(@"\Windows\WinSxS", StringComparison.OrdinalIgnoreCase)) return "Windows Component Store";
        if (path.Contains(@"\Windows\Installer", StringComparison.OrdinalIgnoreCase)) return "Windows Installer";
        if (path.Contains(@"\System Volume Information", StringComparison.OrdinalIgnoreCase)) return "System Restore";
        if (path.Contains(@"\$Recycle.Bin", StringComparison.OrdinalIgnoreCase)) return "Recycle Bin";
        return null;
    }

    private static IReadOnlyList<InstalledApplication> LoadInstalledApps()
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }

        var apps = new List<InstalledApplication>();
        var roots = new[]
        {
            (Microsoft.Win32.Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
            (Microsoft.Win32.Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
            (Microsoft.Win32.Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall")
        };

        foreach (var (root, subKeyPath) in roots)
        {
            using var subKey = root.OpenSubKey(subKeyPath);
            if (subKey is null)
            {
                continue;
            }

            foreach (var name in subKey.GetSubKeyNames())
            {
                using var appKey = subKey.OpenSubKey(name);
                var displayName = appKey?.GetValue("DisplayName") as string;
                var installLocation = appKey?.GetValue("InstallLocation") as string;
                if (string.IsNullOrWhiteSpace(displayName) || string.IsNullOrWhiteSpace(installLocation))
                {
                    continue;
                }

                installLocation = installLocation.Trim('"').TrimEnd('\\');
                if (!Directory.Exists(installLocation))
                {
                    continue;
                }

                apps.Add(new InstalledApplication(displayName, installLocation, $@"{root.Name}\{subKeyPath}\{name}"));
            }
        }

        return apps;
    }

    private sealed record InstalledApplication(string DisplayName, string? InstallLocation, string RegistryKey);
}
