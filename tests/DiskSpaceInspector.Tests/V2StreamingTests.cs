using DiskSpaceInspector.Core.Cleanup;
using DiskSpaceInspector.Core.Models;
using DiskSpaceInspector.Core.Scanning;
using DiskSpaceInspector.Core.Windows;
using DiskSpaceInspector.Storage;

namespace DiskSpaceInspector.Tests;

[TestClass]
public sealed class V2StreamingTests
{
    [TestMethod]
    public async Task StartScanAsync_EmitsBatchesAndProgressMetrics()
    {
        using var fixture = new TempDirectory();
        File.WriteAllBytes(Path.Combine(fixture.Path, "a.bin"), new byte[100]);
        File.WriteAllBytes(Path.Combine(fixture.Path, "b.bin"), new byte[200]);

        var scanner = new FileSystemScanner(new WindowsRelationshipResolver());
        var batches = new List<ScanBatch>();
        var progress = new CapturingProgress();

        var completed = await scanner.StartScanAsync(
            Request(fixture.Path, totalBytes: 1000, freeBytes: 400),
            (batch, _) =>
            {
                batches.Add(batch);
                return Task.CompletedTask;
            },
            progress);

        Assert.AreEqual(ScanStatus.Completed, completed.Session.Status);
        Assert.IsTrue(batches.Count > 0);
        Assert.IsTrue(batches.SelectMany(b => b.Nodes).Any(n => n.Name == "a.bin"));
        Assert.IsTrue(progress.Reports.Any(p => p.UsedBytes == 600));
        Assert.IsTrue(progress.Reports.Any(p => p.ProgressFraction > 0));
    }

    [TestMethod]
    public async Task SaveScanBatchAsync_PersistsDataBeforeCompletion()
    {
        using var fixture = new TempDirectory();
        var store = new SqliteScanStore(Path.Combine(fixture.Path, "store.db"));
        var scanId = Guid.NewGuid();
        var volume = Volume(fixture.Path);
        var session = new ScanSession
        {
            Id = scanId,
            RootPath = fixture.Path,
            Status = ScanStatus.Running,
            StartedAtUtc = DateTimeOffset.UtcNow
        };
        var node = Node(1, fixture.Path, 512);
        var classifier = new CleanupClassifier();
        var findings = new[] { classifier.Classify(node)! }.Where(f => f is not null).Cast<CleanupFinding>().ToList();

        await store.BeginScanAsync(session, volume);
        await store.SaveScanBatchAsync(new ScanBatch
        {
            ScanId = scanId,
            BatchNumber = 1,
            Nodes = [node],
            Metrics = new ScanMetrics
            {
                ScanId = scanId,
                VolumeRootPath = volume.RootPath,
                AccountedBytes = 512,
                UsedBytes = 1024
            }
        }, findings, []);

        var loaded = await store.LoadLatestScanAsync();
        var loadedFindings = await store.LoadCleanupFindingsAsync(scanId);

        Assert.IsNotNull(loaded);
        Assert.AreEqual(ScanStatus.Running, loaded.Session.Status);
        Assert.AreEqual(1, loaded.Nodes.Count);
        Assert.AreEqual(findings.Count, loadedFindings.Count);
    }

    [TestMethod]
    public async Task Store_RecordsModifiedAndDeletedChangesAcrossScans()
    {
        using var fixture = new TempDirectory();
        var store = new SqliteScanStore(Path.Combine(fixture.Path, "changes.db"));
        var volume = Volume(fixture.Path);
        var stableId = "file:fixture:1";

        var firstScan = Guid.NewGuid();
        await store.BeginScanAsync(Session(firstScan, fixture.Path), volume);
        await store.SaveScanBatchAsync(Batch(firstScan, volume.RootPath, Node(1, Path.Combine(fixture.Path, "same.bin"), 100, stableId)), [], []);
        await store.CompleteScanAsync(Completed(firstScan, volume, 100));

        var secondScan = Guid.NewGuid();
        await store.BeginScanAsync(Session(secondScan, fixture.Path), volume);
        await store.SaveScanBatchAsync(Batch(secondScan, volume.RootPath, Node(1, Path.Combine(fixture.Path, "same.bin"), 250, stableId)), [], []);
        await store.CompleteScanAsync(Completed(secondScan, volume, 250));

        var secondChanges = await store.LoadChangeRecordsAsync(secondScan);
        Assert.IsTrue(secondChanges.Any(c => c.Kind == ChangeKind.Modified && c.CurrentSizeBytes == 250));

        var thirdScan = Guid.NewGuid();
        await store.BeginScanAsync(Session(thirdScan, fixture.Path), volume);
        await store.SaveScanBatchAsync(new ScanBatch
        {
            ScanId = thirdScan,
            BatchNumber = 1,
            Metrics = new ScanMetrics { ScanId = thirdScan, VolumeRootPath = volume.RootPath }
        }, [], []);
        await store.CompleteScanAsync(Completed(thirdScan, volume, 0));

        var thirdChanges = await store.LoadChangeRecordsAsync(thirdScan);
        Assert.IsTrue(thirdChanges.Any(c => c.Kind == ChangeKind.Deleted && c.StableId == stableId));
    }

    [TestMethod]
    public void RelationshipResolver_EmitsDeveloperArtifactEvidence()
    {
        using var fixture = new TempDirectory();
        var project = Path.Combine(fixture.Path, "Project");
        var nodeModules = Path.Combine(project, "node_modules");
        Directory.CreateDirectory(nodeModules);
        File.WriteAllText(Path.Combine(project, "package.json"), "{}");
        var resolver = new WindowsRelationshipResolver();

        var relationships = resolver.ResolveRelationships(new FileSystemNode
        {
            Id = 42,
            Name = "node_modules",
            FullPath = nodeModules,
            Kind = FileSystemNodeKind.Directory,
            Category = "Folder"
        }, Guid.NewGuid());

        Assert.IsTrue(relationships.Any(r =>
            r.Kind == FileSystemEdgeKind.PackageArtifact &&
            r.Owner.Contains("npm", StringComparison.OrdinalIgnoreCase) &&
            r.Evidence.Confidence >= 0.8));
    }

    private static ScanRequest Request(string rootPath, long totalBytes, long freeBytes)
    {
        return new ScanRequest(new VolumeInfo
        {
            Name = "Fixture",
            RootPath = rootPath,
            DriveType = "Fixed",
            IsReady = true,
            TotalBytes = totalBytes,
            FreeBytes = freeBytes
        })
        {
            MaxConcurrency = 1
        };
    }

    private static VolumeInfo Volume(string rootPath)
    {
        return new VolumeInfo
        {
            Name = "Fixture",
            RootPath = rootPath,
            DriveType = "Fixed",
            IsReady = true,
            TotalBytes = 1024,
            FreeBytes = 0
        };
    }

    private static ScanSession Session(Guid scanId, string rootPath)
    {
        return new ScanSession
        {
            Id = scanId,
            RootPath = rootPath,
            Status = ScanStatus.Running,
            StartedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private static ScanBatch Batch(Guid scanId, string volumeRoot, FileSystemNode node)
    {
        return new ScanBatch
        {
            ScanId = scanId,
            BatchNumber = 1,
            Nodes = [node],
            Metrics = new ScanMetrics
            {
                ScanId = scanId,
                VolumeRootPath = volumeRoot,
                AccountedBytes = Math.Max(node.TotalPhysicalLength, node.PhysicalLength),
                UsedBytes = 1024
            }
        };
    }

    private static ScanCompleted Completed(Guid scanId, VolumeInfo volume, long bytes)
    {
        return new ScanCompleted
        {
            Session = new ScanSession
            {
                Id = scanId,
                RootPath = volume.RootPath,
                Status = ScanStatus.Completed,
                StartedAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1),
                CompletedAtUtc = DateTimeOffset.UtcNow,
                TotalLogicalBytes = bytes,
                TotalPhysicalBytes = bytes,
                FilesScanned = bytes > 0 ? 1 : 0
            },
            Volume = volume,
            FinalMetrics = new ScanMetrics
            {
                ScanId = scanId,
                VolumeRootPath = volume.RootPath,
                AccountedBytes = bytes,
                UsedBytes = 1024
            },
            BatchCount = 1
        };
    }

    private static FileSystemNode Node(long id, string path, long size, string? stableId = null)
    {
        return new FileSystemNode
        {
            Id = id,
            StableId = stableId ?? $"path:{PathIdentity.HashPath(path)}",
            PathHash = PathIdentity.HashPath(path),
            Name = Path.GetFileName(path),
            FullPath = path,
            Kind = FileSystemNodeKind.File,
            Length = size,
            AllocatedLength = size,
            PhysicalLength = size,
            TotalLength = size,
            TotalPhysicalLength = size,
            FileCount = 1,
            LastModifiedUtc = DateTimeOffset.UtcNow,
            Category = "Temporary"
        };
    }

    private sealed class CapturingProgress : IProgress<ScanProgress>
    {
        public List<ScanProgress> Reports { get; } = [];

        public void Report(ScanProgress value)
        {
            Reports.Add(value);
        }
    }
}
