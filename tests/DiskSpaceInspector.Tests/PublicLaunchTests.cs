using System.Reflection;
using System.Text.RegularExpressions;
using DiskSpaceInspector.Core.Cleanup;
using DiskSpaceInspector.Core.Layout;
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
    public void ReleaseMetadata_UsesStableVersion()
    {
        var version = typeof(FileSystemNode).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        Assert.AreEqual("1.1.0", version);
    }

    [TestMethod]
    public void PrivacyFacts_DisableTelemetryAndDescribeLocalDiagnostics()
    {
        Assert.AreEqual("None", PrivacyAndSafetyFacts.TelemetryMode);
        Assert.IsFalse(PrivacyAndSafetyFacts.NetworkTelemetryEnabled);
        StringAssert.Contains(PrivacyAndSafetyFacts.ExternalIntegrationPolicy, "does not use external");
        CollectionAssert.Contains(PrivacyAndSafetyFacts.BlockedDirectCleanupPaths.ToList(), @"C:\Windows\WinSxS");
    }

    [TestMethod]
    public void TrackedProductFiles_DoNotReferenceRemovedExternalAdvisorBranding()
    {
        var root = FindRepositoryRoot();
        var banned = new[]
        {
            new string(['C', 'o', 'd', 'e', 'x']),
            new string(['C', 'h', 'a', 't', 'G', 'P', 'T']),
            new string(['O', 'p', 'e', 'n', 'A', 'I']),
            string.Concat("OPEN", "AI", "_API_KEY"),
            string.Concat("AI", " cleanup")
        };
        var ignoredSegments = new[]
        {
            $"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}",
            $"{Path.DirectorySeparatorChar}.vs{Path.DirectorySeparatorChar}",
            $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
            $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
            $"{Path.DirectorySeparatorChar}artifacts{Path.DirectorySeparatorChar}"
        };
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => ignoredSegments.All(segment => !path.Contains(segment, StringComparison.OrdinalIgnoreCase)))
            .Where(path => !Path.GetFileName(path).Equals("PublicLaunchTests.cs", StringComparison.OrdinalIgnoreCase))
            .Where(IsTextLike)
            .ToList();

        var hits = new List<string>();
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            hits.AddRange(banned
                .Where(term => text.Contains(term, StringComparison.OrdinalIgnoreCase))
                .Select(term => $"{Path.GetRelativePath(root, file)} contains removed external advisor wording"));
        }

        Assert.AreEqual(0, hits.Count, string.Join(Environment.NewLine, hits.Take(20)));
    }

    [TestMethod]
    public void TrackedProductFiles_UseCorrectOwnerName()
    {
        var root = FindRepositoryRoot();
        var oldOwnerPattern = new Regex(@"\b" + new string(['A', 'n', 'd', 'r', 'e']) + @"\b", RegexOptions.CultureInvariant);
        var correctOwner = "Andrew Rainsberger";
        var ignoredSegments = new[]
        {
            $"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}",
            $"{Path.DirectorySeparatorChar}.vs{Path.DirectorySeparatorChar}",
            $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
            $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
            $"{Path.DirectorySeparatorChar}artifacts{Path.DirectorySeparatorChar}"
        };

        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => ignoredSegments.All(segment => !path.Contains(segment, StringComparison.OrdinalIgnoreCase)))
            .Where(path => !Path.GetFileName(path).Equals("PublicLaunchTests.cs", StringComparison.OrdinalIgnoreCase))
            .Where(IsTextLike)
            .ToList();

        var hits = files
            .Where(file => oldOwnerPattern.IsMatch(File.ReadAllText(file)))
            .Select(file => Path.GetRelativePath(root, file))
            .ToList();

        Assert.AreEqual(0, hits.Count, string.Join(Environment.NewLine, hits.Take(20)));
        StringAssert.Contains(File.ReadAllText(Path.Combine(root, "LICENSE")), correctOwner);
    }

    [TestMethod]
    public async Task AppSettingsStore_PersistsConsumerOptions()
    {
        using var temp = new TempDirectory();
        var store = new JsonAppSettingsStore(Path.Combine(temp.Path, "settings.json"));
        var settings = new AppSettings
        {
            Launch = { OpenDemoWorkspaceOnStartup = true },
            Scan = { IncludeRemovableDrives = false },
            Charts = { MaxBestInsightCards = 12, Density = "Compact" },
            Privacy = { RedactUserProfileInReports = false }
        };

        await store.SaveAsync(settings);
        var loaded = await store.LoadAsync();

        Assert.IsTrue(loaded.Launch.OpenDemoWorkspaceOnStartup);
        Assert.IsFalse(loaded.Scan.IncludeRemovableDrives);
        Assert.AreEqual(12, loaded.Charts.MaxBestInsightCards);
        Assert.AreEqual("Compact", loaded.Charts.Density);
        Assert.IsFalse(loaded.Privacy.RedactUserProfileInReports);
    }

    [TestMethod]
    public async Task CleanupReviewQueue_StagesOnlySafeAndReviewFindings()
    {
        using var temp = new TempDirectory();
        var service = new CleanupReviewQueueService();
        var queue = new CleanupReviewQueue();
        var safe = Finding(ScanWithPath(@"C:\Users\demo\AppData\Local\Temp\a.tmp").Nodes[1], CleanupSafety.Safe);
        var blocked = Finding(ScanWithPath(@"C:\Windows\WinSxS\file.dll").Nodes[1], CleanupSafety.Blocked);
        var system = Finding(ScanWithPath(@"C:\Windows\SoftwareDistribution\cache.bin").Nodes[1], CleanupSafety.UseSystemCleanup);

        var safeResult = service.TryStage(queue, safe);
        var blockedResult = service.TryStage(queue, blocked);
        var systemResult = service.TryStage(queue, system);

        Assert.IsTrue(safeResult.Added);
        Assert.AreEqual(1, queue.Items.Count);
        Assert.IsFalse(blockedResult.Added);
        Assert.IsFalse(systemResult.Added);
        Assert.IsTrue(blockedResult.Message.Contains("Blocked", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(systemResult.Message.Contains("Windows", StringComparison.OrdinalIgnoreCase));

        var export = await service.ExportAsync(queue, temp.Path, PathPrivacyMode.RedactedUserProfile);
        Assert.IsTrue(File.Exists(export.SummaryPath));
        Assert.IsTrue(File.Exists(export.DataPath));
    }

    [TestMethod]
    public void ChangeSummary_TreatsFirstFullScanAsBaseline()
    {
        var scanId = Guid.NewGuid();
        var changes = Enumerable.Range(1, 40)
            .Select(index => new ChangeRecord
            {
                ScanId = scanId,
                StableId = $"path:{index}",
                Path = $@"C:\fixture\file{index}.bin",
                Kind = ChangeKind.Added,
                CurrentSizeBytes = 100,
                Reason = "first time seen"
            })
            .ToList();

        var summary = ChangeSummaryService.Summarize(changes, nodeCount: 45);

        Assert.IsTrue(summary.IsBaseline);
        StringAssert.Contains(summary.Message, "baseline");
    }

    [TestMethod]
    public void DefaultChartTitles_AvoidAnalyticsJargon()
    {
        var service = new StorageAnalyticsService();
        var fixture = ScanWithPath(@"C:\Users\demo\Downloads\movie.iso");
        var snapshot = service.BuildSnapshot(
            fixture,
            [Finding(fixture.Nodes[1], CleanupSafety.Review)],
            [],
            [],
            [fixture.Volume]);
        var defaultTitles = snapshot.Charts
            .Where(chart => !chart.IsAdvanced)
            .Select(chart => chart.Title)
            .ToList();

        CollectionAssert.DoesNotContain(defaultTitles, "Top folder Pareto");
        CollectionAssert.DoesNotContain(defaultTitles, "Subtree entropy map");
        CollectionAssert.DoesNotContain(defaultTitles, "Cleanup effort/value matrix");
    }

    [TestMethod]
    public void VisualLabCards_DoNotUseOldFixedOverflowLayout()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "DiskSpaceInspector.App", "MainWindow.xaml"));

        Assert.IsFalse(xaml.Contains("Height=\"282\"", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(xaml.Contains("MaxHeight=\"34\"", StringComparison.OrdinalIgnoreCase));
        StringAssert.Contains(xaml, "MinHeight=\"382\"");
        StringAssert.Contains(xaml, "Height=\"250\"");
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

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "DiskSpaceInspector.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        Assert.Inconclusive("Repository root was not found from the test output directory.");
        return AppContext.BaseDirectory;
    }

    private static bool IsTextLike(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return extension is ".cs" or ".xaml" or ".md" or ".yml" or ".yaml" or ".ps1" or ".iss" or ".props" or ".csproj" or ".sln" or ".json" or ".gitignore";
    }

    private static long GiB(double value)
    {
        return (long)(value * 1024 * 1024 * 1024);
    }
}
