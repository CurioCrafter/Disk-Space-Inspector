using DiskSpaceInspector.Core.Models;
using DiskSpaceInspector.Storage;

namespace DiskSpaceInspector.Tests;

[TestClass]
public sealed class StorageTests
{
    [TestMethod]
    public async Task SqliteScanStore_RoundTripsScanAndFindings()
    {
        using var fixture = new TempDirectory();
        var databasePath = Path.Combine(fixture.Path, "DiskSpaceInspector.db");
        var store = new SqliteScanStore(databasePath);
        var scanId = Guid.NewGuid();
        var result = new ScanResult
        {
            Session = new ScanSession
            {
                Id = scanId,
                RootPath = fixture.Path,
                Status = ScanStatus.Completed,
                StartedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
                CompletedAtUtc = DateTimeOffset.UtcNow,
                TotalLogicalBytes = 100,
                TotalPhysicalBytes = 100,
                FilesScanned = 1,
                DirectoriesScanned = 1
            },
            Volume = new VolumeInfo
            {
                Name = "Fixture",
                RootPath = fixture.Path,
                DriveType = "Fixed",
                IsReady = true,
                TotalBytes = 1000,
                FreeBytes = 900
            },
            Nodes =
            {
                new FileSystemNode
                {
                    Id = 1,
                    Name = "Fixture",
                    FullPath = fixture.Path,
                    Kind = FileSystemNodeKind.Drive,
                    TotalLength = 100,
                    TotalPhysicalLength = 100,
                    Category = "Drive"
                },
                new FileSystemNode
                {
                    Id = 2,
                    ParentId = 1,
                    Name = "cache.bin",
                    FullPath = Path.Combine(fixture.Path, "cache.bin"),
                    Kind = FileSystemNodeKind.File,
                    Length = 100,
                    PhysicalLength = 100,
                    TotalLength = 100,
                    TotalPhysicalLength = 100,
                    FileCount = 1,
                    Category = "Temporary"
                }
            }
        };
        var finding = new CleanupFinding
        {
            NodeId = 2,
            Path = result.Nodes[1].FullPath,
            DisplayName = "cache.bin",
            Category = "Temporary files",
            Safety = CleanupSafety.Safe,
            RecommendedAction = CleanupActionKind.ClearCache,
            SizeBytes = 100,
            Confidence = 0.9,
            Explanation = "Fixture",
            MatchedRule = "test",
            Evidence = { ["source"] = "test" }
        };

        await store.SaveScanAsync(result, [finding]);
        var loaded = await store.LoadLatestScanAsync();
        var findings = await store.LoadCleanupFindingsAsync(scanId);

        Assert.IsNotNull(loaded);
        Assert.AreEqual(2, loaded.Nodes.Count);
        Assert.AreEqual(scanId, loaded.Session.Id);
        Assert.AreEqual(1, findings.Count);
        Assert.AreEqual(CleanupSafety.Safe, findings[0].Safety);
    }

    [TestMethod]
    public async Task SqliteScanStore_LoadsDashboardAndPagedVisualizerQueries()
    {
        using var fixture = new TempDirectory();
        var databasePath = Path.Combine(fixture.Path, "queries.db");
        var store = new SqliteScanStore(databasePath);
        var scanId = Guid.NewGuid();
        var rootStableId = "root-stable";
        var childPath = Path.Combine(fixture.Path, "Downloads");
        var result = new ScanResult
        {
            Session = new ScanSession
            {
                Id = scanId,
                RootPath = fixture.Path,
                Status = ScanStatus.Completed,
                StartedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
                CompletedAtUtc = DateTimeOffset.UtcNow,
                TotalLogicalBytes = 350,
                TotalPhysicalBytes = 350,
                FilesScanned = 2,
                DirectoriesScanned = 2
            },
            Volume = new VolumeInfo
            {
                Name = "Fixture",
                RootPath = fixture.Path,
                DriveType = "Fixed",
                IsReady = true,
                TotalBytes = 1000,
                FreeBytes = 650
            },
            Nodes =
            {
                new FileSystemNode
                {
                    Id = 1,
                    StableId = rootStableId,
                    PathHash = "root-hash",
                    Name = "Fixture",
                    FullPath = fixture.Path,
                    Kind = FileSystemNodeKind.Drive,
                    TotalLength = 350,
                    TotalPhysicalLength = 350,
                    Category = "Drive"
                },
                new FileSystemNode
                {
                    Id = 2,
                    StableId = "downloads-stable",
                    PathHash = "downloads-hash",
                    ParentStableId = rootStableId,
                    ParentPathHash = "root-hash",
                    ParentId = 1,
                    Name = "Downloads",
                    FullPath = childPath,
                    Kind = FileSystemNodeKind.Directory,
                    TotalLength = 350,
                    TotalPhysicalLength = 350,
                    FolderCount = 0,
                    FileCount = 2,
                    Depth = 1,
                    Category = "Folder"
                },
                new FileSystemNode
                {
                    Id = 3,
                    StableId = "video-stable",
                    PathHash = "video-hash",
                    ParentStableId = "downloads-stable",
                    ParentPathHash = "downloads-hash",
                    ParentId = 2,
                    Name = "clip.mp4",
                    FullPath = Path.Combine(childPath, "clip.mp4"),
                    Kind = FileSystemNodeKind.File,
                    Extension = ".mp4",
                    Length = 250,
                    PhysicalLength = 250,
                    TotalLength = 250,
                    TotalPhysicalLength = 250,
                    FileCount = 1,
                    Depth = 2,
                    LastModifiedUtc = DateTimeOffset.UtcNow.AddDays(-10),
                    Category = "Video"
                },
                new FileSystemNode
                {
                    Id = 4,
                    StableId = "archive-stable",
                    PathHash = "archive-hash",
                    ParentStableId = "downloads-stable",
                    ParentPathHash = "downloads-hash",
                    ParentId = 2,
                    Name = "old.zip",
                    FullPath = Path.Combine(childPath, "old.zip"),
                    Kind = FileSystemNodeKind.File,
                    Extension = ".zip",
                    Length = 100,
                    PhysicalLength = 100,
                    TotalLength = 100,
                    TotalPhysicalLength = 100,
                    FileCount = 1,
                    Depth = 2,
                    LastModifiedUtc = DateTimeOffset.UtcNow.AddYears(-2),
                    Category = "Archive"
                }
            }
        };
        var finding = new CleanupFinding
        {
            NodeId = 4,
            Path = result.Nodes[3].FullPath,
            DisplayName = "old.zip",
            Category = "Downloads review",
            Safety = CleanupSafety.Review,
            RecommendedAction = CleanupActionKind.ReviewDownloads,
            SizeBytes = 100,
            Confidence = 0.8,
            Explanation = "Fixture",
            MatchedRule = "test"
        };

        await store.SaveScanAsync(result, [finding]);

        var dashboard = await store.LoadDriveDashboardAsync();
        var rootChildren = await store.LoadChildrenAsync(rootStableId);
        var search = await store.SearchNodesAsync("clip");
        var treemap = await store.LoadTreemapDataAsync(rootStableId);
        var sunburst = await store.LoadSunburstDataAsync(rootStableId);
        var types = await store.LoadTypeBreakdownAsync();
        var ages = await store.LoadAgeHistogramAsync();

        Assert.IsNotNull(dashboard);
        Assert.AreEqual(scanId, dashboard.ScanId);
        Assert.IsTrue(dashboard.TopSpaceConsumers.Count > 0);
        Assert.AreEqual(1, rootChildren.Count);
        Assert.AreEqual("Downloads", rootChildren[0].Name);
        Assert.AreEqual("clip.mp4", search.Single().Name);
        Assert.IsTrue(treemap.Any(n => n.Name == "Downloads"));
        Assert.IsTrue(sunburst.Any(n => n.Name == "old.zip"));
        Assert.IsTrue(types.Any(t => t.Label == "Video"));
        Assert.IsTrue(ages.Count > 0);
    }
}
