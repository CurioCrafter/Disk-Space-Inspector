using DiskSpaceInspector.Core.Layout;
using DiskSpaceInspector.Core.Models;

namespace DiskSpaceInspector.Tests;

[TestClass]
public sealed class StorageAnalyticsServiceTests
{
    [TestMethod]
    public void BuildSnapshot_ReturnsPremiumChartSetWithData()
    {
        var fixture = Fixture();
        var service = new StorageAnalyticsService();

        var snapshot = service.BuildSnapshot(
            fixture.Scan,
            fixture.Findings,
            fixture.Relationships,
            fixture.Changes,
            fixture.Volumes);

        Assert.IsTrue(snapshot.Charts.Count >= 24);
        Assert.IsTrue(snapshot.Tutorials.Count >= 6);
        Assert.IsTrue(snapshot.Charts.All(HasData));
        Assert.IsTrue(snapshot.Charts.Select(c => c.Kind).Distinct().Count() >= 10);
        Assert.IsTrue(snapshot.Charts.Any(c => c.Key == "reclaimable-safety-funnel"));
        Assert.IsTrue(snapshot.Charts.Any(c => c.Key == "app-ownership-sankey"));
        Assert.IsTrue(snapshot.Charts.Any(c => c.Key == "dependency-cache-comparison"));
    }

    [TestMethod]
    public void BuildSnapshot_IsDeterministicForStableInput()
    {
        var fixture = Fixture();
        var service = new StorageAnalyticsService();

        var first = service.BuildSnapshot(fixture.Scan, fixture.Findings, fixture.Relationships, fixture.Changes, fixture.Volumes);
        var second = service.BuildSnapshot(fixture.Scan, fixture.Findings, fixture.Relationships, fixture.Changes, fixture.Volumes);

        CollectionAssert.AreEqual(first.Charts.Select(c => c.Key).ToList(), second.Charts.Select(c => c.Key).ToList());
        CollectionAssert.AreEqual(first.Charts.Select(c => c.Title).ToList(), second.Charts.Select(c => c.Title).ToList());
    }

    [TestMethod]
    public void CleanupCharts_KeepBlockedAndSystemItemsInGuardrailLanes()
    {
        var fixture = Fixture();
        var service = new StorageAnalyticsService();

        var snapshot = service.BuildSnapshot(fixture.Scan, fixture.Findings, fixture.Relationships, fixture.Changes, fixture.Volumes);
        var funnel = snapshot.Charts.Single(c => c.Key == "reclaimable-safety-funnel");
        var blocked = funnel.Points.Single(p => p.Label == "Blocked");
        var system = funnel.Points.Single(p => p.Label == "Use system cleanup");

        Assert.AreEqual("Blocked", blocked.ColorKey);
        Assert.AreEqual("System", system.ColorKey);
        Assert.IsTrue(blocked.SizeBytes > 0);
        Assert.IsTrue(system.SizeBytes > 0);
    }

    private static bool HasData(ChartDefinition chart)
    {
        return chart.Points.Count > 0 ||
               chart.Cells.Count > 0 ||
               chart.Flows.Count > 0 ||
               chart.Series.Count > 0 ||
               chart.Metrics.Count > 0;
    }

    private static AnalyticsFixture Fixture()
    {
        var scanId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var nodes = new List<FileSystemNode>
        {
            Node(1, null, @"C:\", "Windows", FileSystemNodeKind.Drive, 500, "Drive", now),
            Node(2, 1, @"C:\Users", "Users", FileSystemNodeKind.Directory, 180, "Folder", now.AddDays(-2), files: 10_000, folders: 600),
            Node(3, 2, @"C:\Users\demo\Downloads", "Downloads", FileSystemNodeKind.Directory, 80, "Archive", now.AddDays(-120), files: 350, folders: 12),
            Node(4, 3, @"C:\Users\demo\Downloads\installer.iso", "installer.iso", FileSystemNodeKind.File, 20, "Archive", now.AddDays(-210), ".iso"),
            Node(5, 2, @"C:\Users\demo\AppData\Local\Temp", "Temp", FileSystemNodeKind.Directory, 14, "Temporary", now.AddDays(-12), files: 8_000, folders: 400),
            Node(6, 1, @"C:\Projects\shop-ui\node_modules", "node_modules", FileSystemNodeKind.Directory, 46, "Code", now.AddDays(-7), files: 120_000, folders: 16_000),
            Node(7, 1, @"C:\Projects\shop-ui\.next", ".next", FileSystemNodeKind.Directory, 9, "Code", now.AddHours(-4), files: 24_000, folders: 900),
            Node(8, 1, @"C:\ProgramData\DockerDesktop", "DockerDesktop", FileSystemNodeKind.Directory, 38, "Archive", now.AddDays(-1), files: 2_000, folders: 180),
            Node(9, 8, @"C:\ProgramData\DockerDesktop\wsl-data.vhdx", "wsl-data.vhdx", FileSystemNodeKind.File, 30, "Archive", now.AddDays(-1), ".vhdx"),
            Node(10, 1, @"C:\Windows\SoftwareDistribution\Download", "SoftwareDistribution", FileSystemNodeKind.Directory, 18, "System", now.AddDays(-6), files: 4_000, folders: 180),
            Node(11, 1, @"C:\Windows\WinSxS", "WinSxS", FileSystemNodeKind.Directory, 32, "System", now.AddHours(-2), files: 90_000, folders: 20_000),
            Node(12, 1, @"C:\Users\demo\OneDrive", "OneDrive", FileSystemNodeKind.Directory, 44, "Image", now.AddDays(-90), files: 4_400, folders: 260),
            Node(13, 12, @"C:\Users\demo\OneDrive\Photos\raw.zip", "raw.zip", FileSystemNodeKind.File, 12, "Image", now.AddDays(-390), ".zip"),
            Node(14, 1, @"C:\Users\demo\.nuget\packages", "NuGet", FileSystemNodeKind.Directory, 7, "Code", now.AddDays(-18), files: 22_000, folders: 3_600),
            Node(15, 1, @"C:\Users\demo\AppData\Local\pip\Cache", "pip", FileSystemNodeKind.Directory, 5, "Code", now.AddDays(-22), files: 8_000, folders: 640),
            Node(16, 1, @"C:\ProgramData\Package Cache", "Package Cache", FileSystemNodeKind.Directory, 25, "Installer", now.AddDays(-170), files: 1_000, folders: 90)
        };

        var scan = new ScanResult
        {
            Session = new ScanSession
            {
                Id = scanId,
                RootPath = @"C:\",
                Status = ScanStatus.Completed,
                StartedAtUtc = now.AddMinutes(-5),
                CompletedAtUtc = now,
                TotalPhysicalBytes = GiB(500),
                FilesScanned = 280_000,
                DirectoriesScanned = 44_000,
                IssueCount = 2
            },
            Volume = new VolumeInfo { Name = "Windows", Label = "Windows", RootPath = @"C:\", IsReady = true, TotalBytes = GiB(952), FreeBytes = GiB(452), DriveType = "Fixed", FileSystem = "NTFS" },
            Nodes = nodes,
            Issues =
            [
                new ScanIssue { Path = @"C:\System Volume Information", Operation = "Enumerate", Message = "Access denied" }
            ]
        };

        var findings = new List<CleanupFinding>
        {
            Finding(nodes[4], CleanupSafety.Safe, CleanupActionKind.ClearCache, "Temporary files"),
            Finding(nodes[5], CleanupSafety.Review, CleanupActionKind.ClearCache, "Developer artifact"),
            Finding(nodes[6], CleanupSafety.Safe, CleanupActionKind.ClearCache, "Developer artifact"),
            Finding(nodes[9], CleanupSafety.UseSystemCleanup, CleanupActionKind.RunSystemCleanup, "Windows cleanup"),
            Finding(nodes[10], CleanupSafety.Blocked, CleanupActionKind.LeaveAlone, "System managed storage"),
            Finding(nodes[15], CleanupSafety.Review, CleanupActionKind.ArchiveOrMove, "Installer cache")
        };

        var relationships = new List<StorageRelationship>
        {
            Relationship(scanId, nodes[5], @"C:\Projects\shop-ui\package.json", FileSystemEdgeKind.PackageArtifact, "generated by", "npm", 0.94),
            Relationship(scanId, nodes[6], @"C:\Projects\shop-ui\next.config.js", FileSystemEdgeKind.PackageArtifact, "generated by", "Next.js", 0.92),
            Relationship(scanId, nodes[7], "Docker Desktop", FileSystemEdgeKind.AppOwnership, "owned by", "Docker", 0.86),
            Relationship(scanId, nodes[11], "OneDrive cloud storage", FileSystemEdgeKind.AppOwnership, "synced by", "OneDrive", 0.8),
            Relationship(scanId, nodes[14], @"C:\Projects\ml-lab\requirements.txt", FileSystemEdgeKind.PackageArtifact, "generated by", "pip", 0.82)
        };

        var changes = new List<ChangeRecord>
        {
            Change(scanId, nodes[5], 30, 46, "Dependencies grew"),
            Change(scanId, nodes[7], 18, 38, "Docker images grew"),
            Change(scanId, nodes[4], 20, 14, "Temp cleanup reduced files"),
            Change(scanId, nodes[9], 7, 18, "Windows update cache grew")
        };

        var volumes = new List<VolumeInfo>
        {
            scan.Volume,
            new() { Name = "Data", Label = "Projects", RootPath = @"D:\", IsReady = true, TotalBytes = GiB(1_000), FreeBytes = GiB(400), DriveType = "Fixed", FileSystem = "NTFS" }
        };

        return new AnalyticsFixture(scan, findings, relationships, changes, volumes);
    }

    private static FileSystemNode Node(long id, long? parentId, string path, string name, FileSystemNodeKind kind, double gib, string category, DateTimeOffset modified, string? extension = null, int files = 1, int folders = 0)
    {
        return new FileSystemNode
        {
            Id = id,
            StableId = $"node-{id}",
            ParentId = parentId,
            ParentStableId = parentId is null ? null : $"node-{parentId}",
            FullPath = path,
            Name = name,
            Kind = kind,
            Extension = extension,
            Category = category,
            Length = kind == FileSystemNodeKind.File ? GiB(gib) : 0,
            PhysicalLength = kind == FileSystemNodeKind.File ? GiB(gib) : 0,
            TotalLength = GiB(gib),
            TotalPhysicalLength = GiB(gib),
            FileCount = files,
            FolderCount = folders,
            Depth = path.Count(c => c == '\\'),
            LastModifiedUtc = modified
        };
    }

    private static CleanupFinding Finding(FileSystemNode node, CleanupSafety safety, CleanupActionKind action, string category)
    {
        return new CleanupFinding
        {
            NodeId = node.Id,
            Path = node.FullPath,
            DisplayName = node.Name,
            Category = category,
            Safety = safety,
            RecommendedAction = action,
            SizeBytes = node.TotalPhysicalLength,
            FileCount = node.FileCount,
            LastModifiedUtc = node.LastModifiedUtc,
            Confidence = safety == CleanupSafety.Blocked ? 1 : 0.84,
            Explanation = $"{category} fixture",
            MatchedRule = category.Replace(" ", "")
        };
    }

    private static StorageRelationship Relationship(Guid scanId, FileSystemNode node, string target, FileSystemEdgeKind kind, string label, string owner, double confidence)
    {
        return new StorageRelationship
        {
            ScanId = scanId,
            SourceNodeId = node.Id,
            SourcePath = node.FullPath,
            TargetPath = target,
            Kind = kind,
            Label = label,
            Owner = owner,
            Evidence = new RelationshipEvidence { Source = "test", Detail = "fixture", Confidence = confidence }
        };
    }

    private static ChangeRecord Change(Guid scanId, FileSystemNode node, double previous, double current, string reason)
    {
        return new ChangeRecord
        {
            ScanId = scanId,
            StableId = node.StableId,
            Path = node.FullPath,
            Kind = ChangeKind.Modified,
            PreviousSizeBytes = GiB(previous),
            CurrentSizeBytes = GiB(current),
            Reason = reason
        };
    }

    private static long GiB(double value)
    {
        return (long)(value * 1024 * 1024 * 1024);
    }

    private sealed record AnalyticsFixture(
        ScanResult Scan,
        IReadOnlyList<CleanupFinding> Findings,
        IReadOnlyList<StorageRelationship> Relationships,
        IReadOnlyList<ChangeRecord> Changes,
        IReadOnlyList<VolumeInfo> Volumes);
}
