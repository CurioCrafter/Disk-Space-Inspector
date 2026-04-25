using System.Reflection;
using DiskSpaceInspector.Core.Models;
using DiskSpaceInspector.Core.Reporting;
using DiskSpaceInspector.Core.State;

namespace DiskSpaceInspector.Tests;

[TestClass]
public sealed class PublicLaunchTests
{
    [TestMethod]
    public async Task ReportExport_RedactsUserProfilePathsByDefault()
    {
        using var temp = new TempDirectory();
        var userPath = $@"C:\Users\{Environment.UserName}\Downloads\private.iso";
        var scan = ScanWithPath(userPath);
        var finding = Finding(scan.Nodes[1], CleanupSafety.Review);
        var service = new ReportExportService();

        var bundle = await service.ExportAsync(
            scan,
            [finding],
            [],
            [],
            [],
            new ReportExportOptions
            {
                OutputDirectory = temp.Path,
                PathPrivacyMode = PathPrivacyMode.RedactedUserProfile
            });

        Assert.IsTrue(File.Exists(bundle.SummaryPath));
        Assert.IsTrue(File.Exists(bundle.DataPath));
        Assert.AreEqual(2, bundle.FileCount);
        Assert.IsTrue(bundle.RedactedPathCount > 0);

        var allText = await File.ReadAllTextAsync(bundle.SummaryPath) + await File.ReadAllTextAsync(bundle.DataPath);
        StringAssert.Contains(allText, "%USERPROFILE%");
        Assert.IsFalse(allText.Contains($@"C:\Users\{Environment.UserName}", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task FirstRunStateStore_PersistsLaunchAndDemoState()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "first-run.json");
        var store = new JsonFirstRunStateStore(path);
        var state = new FirstRunState
        {
            HasOpenedApp = true,
            HasLoadedDemo = true,
            FirstOpenedAtUtc = DateTimeOffset.Parse("2026-04-24T00:00:00Z"),
            LastDemoLoadedAtUtc = DateTimeOffset.Parse("2026-04-24T00:05:00Z")
        };

        await store.SaveAsync(state);
        var loaded = await store.LoadAsync();

        Assert.IsTrue(loaded.HasOpenedApp);
        Assert.IsTrue(loaded.HasLoadedDemo);
        Assert.AreEqual(state.FirstOpenedAtUtc, loaded.FirstOpenedAtUtc);
        Assert.AreEqual(state.LastDemoLoadedAtUtc, loaded.LastDemoLoadedAtUtc);
    }

    [TestMethod]
    public void ReleaseMetadata_UsesPreviewVersion()
    {
        var version = typeof(FileSystemNode).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        Assert.AreEqual("0.5.0-preview.1", version);
    }

    [TestMethod]
    public void PrivacyFacts_DisableTelemetryAndDescribeLocalDiagnostics()
    {
        Assert.AreEqual("None", PrivacyAndSafetyFacts.TelemetryMode);
        Assert.IsFalse(PrivacyAndSafetyFacts.NetworkTelemetryEnabled);
        StringAssert.Contains(PrivacyAndSafetyFacts.CodexCredentialPolicy, "never reads or stores Codex OAuth tokens");
        CollectionAssert.Contains(PrivacyAndSafetyFacts.BlockedDirectCleanupPaths.ToList(), @"C:\Windows\WinSxS");
    }

    private static ScanResult ScanWithPath(string path)
    {
        var scanId = Guid.NewGuid();
        var root = new FileSystemNode
        {
            Id = 1,
            StableId = "root",
            Name = "Windows",
            FullPath = @"C:\",
            Kind = FileSystemNodeKind.Drive,
            TotalPhysicalLength = GiB(100),
            TotalLength = GiB(100),
            FileCount = 1,
            FolderCount = 1,
            Category = "Drive"
        };
        var file = new FileSystemNode
        {
            Id = 2,
            StableId = "file",
            ParentId = 1,
            ParentStableId = "root",
            Name = Path.GetFileName(path),
            FullPath = path,
            Kind = FileSystemNodeKind.File,
            Extension = ".iso",
            Length = GiB(5),
            PhysicalLength = GiB(5),
            TotalLength = GiB(5),
            TotalPhysicalLength = GiB(5),
            FileCount = 1,
            Category = "Archive",
            LastModifiedUtc = DateTimeOffset.UtcNow.AddDays(-90)
        };

        return new ScanResult
        {
            Session = new ScanSession
            {
                Id = scanId,
                RootPath = path,
                Status = ScanStatus.Completed,
                StartedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
                CompletedAtUtc = DateTimeOffset.UtcNow,
                FilesScanned = 1,
                DirectoriesScanned = 1,
                TotalPhysicalBytes = GiB(100)
            },
            Volume = new VolumeInfo
            {
                Name = "Windows",
                RootPath = @"C:\",
                DriveType = "Fixed",
                FileSystem = "NTFS",
                IsReady = true,
                TotalBytes = GiB(200),
                FreeBytes = GiB(100)
            },
            Nodes = [root, file]
        };
    }

    private static CleanupFinding Finding(FileSystemNode node, CleanupSafety safety)
    {
        return new CleanupFinding
        {
            NodeId = node.Id,
            Path = node.FullPath,
            DisplayName = node.Name,
            Category = "Downloads review",
            Safety = safety,
            RecommendedAction = CleanupActionKind.ReviewDownloads,
            SizeBytes = node.TotalPhysicalLength,
            FileCount = 1,
            Confidence = 0.8,
            Explanation = "Fixture finding.",
            MatchedRule = "DownloadsLargeOld",
            AppOrSource = "User files"
        };
    }

    private static long GiB(double value)
    {
        return (long)(value * 1024 * 1024 * 1024);
    }
}
