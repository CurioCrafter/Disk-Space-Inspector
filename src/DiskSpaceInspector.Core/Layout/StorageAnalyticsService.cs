using DiskSpaceInspector.Core.Models;
using DiskSpaceInspector.Core.Services;

namespace DiskSpaceInspector.Core.Layout;

public sealed class StorageAnalyticsService : IStorageAnalyticsService
{
    private static readonly StringComparer IgnoreCase = StringComparer.OrdinalIgnoreCase;

    public StorageAnalyticsSnapshot BuildSnapshot(
        ScanResult scan,
        IReadOnlyList<CleanupFinding> findings,
        IReadOnlyList<StorageRelationship> relationships,
        IReadOnlyList<ChangeRecord> changes,
        IReadOnlyList<VolumeInfo> volumes)
    {
        var nodes = scan.Nodes;
        var files = nodes.Where(n => n.Kind == FileSystemNodeKind.File).ToList();
        var directories = nodes.Where(n => n.Kind != FileSystemNodeKind.File).ToList();
        var totalBytes = Math.Max(1, nodes.Where(n => n.ParentId is null).Select(SizeOf).DefaultIfEmpty(scan.Session.TotalPhysicalBytes).Max());
        var charts = new List<ChartDefinition>
        {
            BuildKpis(scan, findings, relationships, changes),
            BuildDriveCapacity(volumes.Count == 0 ? [scan.Volume] : volumes),
            BuildTopFolderPareto(nodes, totalBytes),
            BuildCumulativeSizeCurve(nodes, totalBytes),
            BuildFolderDepthHeatmap(directories),
            BuildSubtreeEntropyMap(directories, nodes),
            BuildExtensionRank(files),
            BuildCategoryStack(nodes),
            BuildFileCountScatter(directories),
            BuildTinyFileDensity(files),
            BuildLargestFileBubbles(files),
            BuildModifiedAgeTimeline(files),
            BuildCalendarHeatmap(files),
            BuildStaleDecayCurve(files),
            BuildRecentGrowthTimeline(changes),
            BuildDeltaWaterfall(changes),
            BuildReclaimableFunnel(findings),
            BuildCleanupEffortValue(findings),
            BuildSafetyStack(findings),
            BuildBlockedRiskMap(findings),
            BuildOwnershipFlow(relationships, nodes),
            BuildCacheOwnershipMatrix(relationships, findings),
            BuildDeveloperDependencyFlow(relationships, nodes),
            BuildCloudLocalStatus(nodes, relationships),
            BuildPackageManagerFootprint(nodes, findings),
            BuildSystemDevRoots(nodes),
            BuildDependencyCacheRadar(nodes),
            BuildInstallerRiskRadar(findings, nodes)
        };

        return new StorageAnalyticsSnapshot
        {
            Charts = charts,
            Tutorials = BuildTutorials(),
            Summary = $"{charts.Count:n0} visual analytics generated from {nodes.Count:n0} nodes, {findings.Count:n0} cleanup findings, and {relationships.Count:n0} relationship edges."
        };
    }

    private static ChartDefinition BuildKpis(
        ScanResult scan,
        IReadOnlyList<CleanupFinding> findings,
        IReadOnlyList<StorageRelationship> relationships,
        IReadOnlyList<ChangeRecord> changes)
    {
        var safe = findings.Where(f => f.Safety == CleanupSafety.Safe).Sum(f => f.SizeBytes);
        var review = findings.Where(f => f.Safety == CleanupSafety.Review).Sum(f => f.SizeBytes);
        var blocked = findings.Where(f => f.Safety == CleanupSafety.Blocked).Sum(f => f.SizeBytes);
        return Chart(
            "lab-kpis",
            "Storage intelligence summary",
            "Best insights",
            "Scan health, reclaimable lanes, relationship evidence, and change volume at a glance.",
            VisualChartKind.KpiStrip,
            metrics:
            [
                Metric("Accounted", FormatBytes(scan.Session.TotalPhysicalBytes), $"{scan.Session.FilesScanned:n0} files", "Drive"),
                Metric("Safe reclaim", FormatBytes(safe), "Low risk staged cleanup", "Safe"),
                Metric("Review queue", FormatBytes(review), "Manual review recommended", "Review"),
                Metric("Blocked", FormatBytes(blocked), "System/app state guarded", "Blocked"),
                Metric("Relationships", relationships.Count.ToString("n0"), "Evidence-backed edges", "Link"),
                Metric("Changes", changes.Count.ToString("n0"), "Snapshot deltas", "Archive")
            ],
            insight: "Start with safe reclaim, then review large owner-linked items. Blocked space is intentionally not actionable.");
    }

    private static ChartDefinition BuildDriveCapacity(IReadOnlyList<VolumeInfo> volumes)
    {
        var points = volumes
            .Where(v => v.TotalBytes > 0)
            .Select(v =>
            {
                var used = Math.Max(0, v.TotalBytes - v.FreeBytes);
                return Point(v.DisplayName, used, v.TotalBytes, "Drive", FormatBytes(used), v.RootPath, null, used / (double)Math.Max(1, v.TotalBytes));
            })
            .ToList();
        return Chart("drive-capacity-rings", "Drive capacity rings", "Space", "Used vs free space by discovered drive.", VisualChartKind.Donut, points, insight: "Fuller rings deserve the first scan or cleanup review.");
    }

    private static ChartDefinition BuildTopFolderPareto(IReadOnlyList<FileSystemNode> nodes, long totalBytes)
    {
        var cumulative = 0L;
        var points = nodes
            .Where(n => n.ParentId is not null && n.Kind != FileSystemNodeKind.File && SizeOf(n) > 0)
            .OrderByDescending(SizeOf)
            .ThenBy(n => n.Name, IgnoreCase)
            .Take(12)
            .Select(n =>
            {
                cumulative += SizeOf(n);
                return Point(n.Name, SizeOf(n), cumulative, n.Category, FormatBytes(SizeOf(n)), n.FullPath, n.Id, cumulative / (double)totalBytes);
            })
            .ToList();
        return Chart("top-folder-pareto", "Top folder Pareto", "Space", "Largest folders plus cumulative share of the drive.", VisualChartKind.RankBar, points, insight: "A few folders usually explain most user-visible pressure.");
    }

    private static ChartDefinition BuildCumulativeSizeCurve(IReadOnlyList<FileSystemNode> nodes, long totalBytes)
    {
        var running = 0L;
        var index = 0;
        var points = nodes
            .Where(n => n.ParentId is not null && SizeOf(n) > 0)
            .OrderByDescending(SizeOf)
            .Take(80)
            .Select(n =>
            {
                running += SizeOf(n);
                index++;
                return Point(index.ToString(), index, running, n.Category, $"{running / (double)totalBytes:P0}", n.FullPath, n.Id, running / (double)totalBytes);
            })
            .ToList();
        return Chart("cumulative-size-curve", "Cumulative size curve", "Space", "How quickly large items account for the scan.", VisualChartKind.Curve, points, insight: "A steep early curve means targeted cleanup beats broad file hunting.");
    }

    private static ChartDefinition BuildFolderDepthHeatmap(IReadOnlyList<FileSystemNode> directories)
    {
        var cells = directories
            .Where(n => SizeOf(n) > 0)
            .GroupBy(n => new { Depth = Math.Min(n.Depth, 6), Category = CleanCategory(n.Category) })
            .Select(g => Cell($"Depth {g.Key.Depth}", g.Key.Category, g.Sum(SizeOf), g.Count(), g.Key.Category))
            .ToList();
        return Chart("folder-depth-heatmap", "Folder depth heatmap", "Space", "Where large folders sit by depth and category.", VisualChartKind.Heatmap, cells: cells, insight: "Deep hot cells often point at generated caches or dependency folders.");
    }

    private static ChartDefinition BuildSubtreeEntropyMap(IReadOnlyList<FileSystemNode> directories, IReadOnlyList<FileSystemNode> nodes)
    {
        var children = nodes.Where(n => n.ParentId is not null).GroupBy(n => n.ParentId!.Value).ToDictionary(g => g.Key, g => g.ToList());
        var cells = directories
            .Where(d => children.ContainsKey(d.Id))
            .OrderByDescending(SizeOf)
            .Take(18)
            .Select(d =>
            {
                var childList = children[d.Id].Where(c => SizeOf(c) > 0).ToList();
                var total = Math.Max(1, childList.Sum(SizeOf));
                var entropy = childList.Sum(c =>
                {
                    var p = SizeOf(c) / (double)total;
                    return p <= 0 ? 0 : -p * Math.Log(p, 2);
                });
                return new HeatmapCell
                {
                    Row = d.Name,
                    Column = "mix",
                    Label = d.Name,
                    Value = entropy,
                    SizeBytes = SizeOf(d),
                    Count = childList.Count,
                    ColorKey = d.Category,
                    Detail = $"{FormatBytes(SizeOf(d))}, entropy {entropy:n2}"
                };
            })
            .ToList();
        return Chart("subtree-entropy-map", "Subtree entropy map", "Space", "Balanced vs concentrated folders by child-size distribution.", VisualChartKind.Heatmap, cells: cells, insight: "Low entropy means one child dominates; high entropy means broad cleanup needs category tools.");
    }

    private static ChartDefinition BuildExtensionRank(IReadOnlyList<FileSystemNode> files)
    {
        var points = files
            .Where(f => SizeOf(f) > 0)
            .GroupBy(f => string.IsNullOrWhiteSpace(f.Extension) ? "(none)" : f.Extension.ToLowerInvariant())
            .Select(g => Point(g.Key, g.Sum(SizeOf), g.Count(), ExtensionCategory(g.Key), FormatBytes(g.Sum(SizeOf)), "", null))
            .OrderByDescending(p => p.SizeBytes)
            .Take(14)
            .ToList();
        return Chart("extension-rank", "Extension rank", "File makeup", "File extensions ordered by physical size.", VisualChartKind.RankBar, points, insight: "Large extensions reveal whether pressure is media, installers, archives, or developer output.");
    }

    private static ChartDefinition BuildCategoryStack(IReadOnlyList<FileSystemNode> nodes)
    {
        var sizePoints = GroupByCategory(nodes, n => SizeOf(n));
        var countPoints = nodes
            .Where(n => n.Kind == FileSystemNodeKind.File)
            .GroupBy(n => CleanCategory(n.Category))
            .Select(g => Point(g.Key, g.Count(), g.Count(), g.Key, $"{g.Count():n0} files", "", null))
            .OrderByDescending(p => p.Y)
            .Take(10)
            .ToList();
        return Chart(
            "category-stack",
            "Category size vs count",
            "File makeup",
            "Compares bytes and file counts by category.",
            VisualChartKind.StackedBar,
            series:
            [
                new ChartSeries { Name = "Size", ColorKey = "Drive", Points = sizePoints },
                new ChartSeries { Name = "Files", ColorKey = "Folder", Points = countPoints }
            ],
            insight: "A category can be small in bytes but expensive in file-count overhead.");
    }

    private static ChartDefinition BuildFileCountScatter(IReadOnlyList<FileSystemNode> directories)
    {
        var points = directories
            .Where(n => n.FileCount > 0 && SizeOf(n) > 0)
            .OrderByDescending(n => n.FileCount)
            .Take(120)
            .Select(n => Point(n.Name, Math.Log10(Math.Max(1, n.FileCount)), Math.Log10(Math.Max(1, SizeOf(n) / 1024d / 1024d)), n.Category, $"{n.FileCount:n0} files, {FormatBytes(SizeOf(n))}", n.FullPath, n.Id, SizeOf(n)))
            .ToList();
        return Chart("file-count-scatter", "File count vs size", "File makeup", "Folders plotted by file count and megabytes.", VisualChartKind.Scatter, points, insight: "High-count, low-size folders are build/cache cleanup targets.");
    }

    private static ChartDefinition BuildTinyFileDensity(IReadOnlyList<FileSystemNode> files)
    {
        var points = files
            .GroupBy(f => CleanCategory(f.Category))
            .Select(g =>
            {
                var list = g.ToList();
                var tiny = list.Count(f => SizeOf(f) < 256 * 1024);
                var density = list.Count == 0 ? 0 : tiny / (double)list.Count;
                return Point(g.Key, list.Count, density, g.Key, $"{density:P0} tiny", "", null, density);
            })
            .OrderByDescending(p => p.Score)
            .ToList();
        return Chart("tiny-file-density", "Tiny-file density", "File makeup", "Categories with many small files.", VisualChartKind.Scatter, points, insight: "Tiny-file density is a performance signal, not just a space signal.");
    }

    private static ChartDefinition BuildLargestFileBubbles(IReadOnlyList<FileSystemNode> files)
    {
        var points = files
            .Where(f => SizeOf(f) > 0)
            .OrderByDescending(SizeOf)
            .Take(28)
            .Select(f => Point(f.Name, SizeOf(f), SizeOf(f), f.Category, FormatBytes(SizeOf(f)), f.FullPath, f.Id))
            .ToList();
        return Chart("largest-file-bubbles", "Largest-file bubble pack", "File makeup", "Individual heavy files sized as bubbles.", VisualChartKind.BubblePack, points, insight: "Single-file triage is fastest for VHDX, ISO, media, backup, and dump files.");
    }

    private static ChartDefinition BuildModifiedAgeTimeline(IReadOnlyList<FileSystemNode> files)
    {
        var now = DateTimeOffset.UtcNow;
        var points = files
            .Where(f => f.LastModifiedUtc is not null)
            .GroupBy(f => AgeBucket(now, f.LastModifiedUtc!.Value))
            .Select(g => Point(g.Key, AgeBucketIndex(g.Key), g.Sum(SizeOf), "Archive", FormatBytes(g.Sum(SizeOf)), "", null))
            .OrderBy(p => p.X)
            .ToList();
        return Chart("modified-age-timeline", "Modified-age timeline", "Time", "Bytes grouped by file age.", VisualChartKind.Timeline, points, insight: "Old large bytes are good archive/move candidates when ownership is clear.");
    }

    private static ChartDefinition BuildCalendarHeatmap(IReadOnlyList<FileSystemNode> files)
    {
        var cells = files
            .Where(f => f.LastModifiedUtc is not null)
            .GroupBy(f =>
            {
                var date = f.LastModifiedUtc!.Value.LocalDateTime.Date;
                return new { Week = ISOWeekOfYear(date), Day = date.DayOfWeek.ToString()[..3] };
            })
            .Select(g => Cell($"W{g.Key.Week:00}", g.Key.Day, g.Sum(SizeOf), g.Count(), "Archive"))
            .OrderBy(c => c.Row)
            .ThenBy(c => c.Column)
            .ToList();
        return Chart("modified-calendar-heatmap", "Modified calendar heatmap", "Time", "Activity intensity by week and day.", VisualChartKind.CalendarHeatmap, cells: cells, insight: "Clusters can identify imports, downloads, build bursts, or backup days.");
    }

    private static ChartDefinition BuildStaleDecayCurve(IReadOnlyList<FileSystemNode> files)
    {
        var now = DateTimeOffset.UtcNow;
        var buckets = new[] { 7, 30, 90, 180, 365, 730, 1095 };
        var total = Math.Max(1, files.Sum(SizeOf));
        var points = buckets
            .Select(days =>
            {
                var stale = files.Where(f => f.LastModifiedUtc is not null && (now - f.LastModifiedUtc.Value).TotalDays >= days).Sum(SizeOf);
                return Point($"{days}d", days, stale, "Archive", $"{stale / (double)total:P0}", "", null, stale / (double)total);
            })
            .ToList();
        return Chart("stale-decay-curve", "Stale-data decay curve", "Time", "Share of bytes older than each age threshold.", VisualChartKind.Curve, points, insight: "A high 365-day value means archive policy can matter more than cache clearing.");
    }

    private static ChartDefinition BuildRecentGrowthTimeline(IReadOnlyList<ChangeRecord> changes)
    {
        var points = changes
            .OrderBy(c => c.DetectedAtUtc)
            .Select((c, i) => Point(ChangePathName(c), i + 1, c.DeltaBytes, c.DeltaBytes >= 0 ? "Danger" : "Safe", FormatSignedBytes(c.DeltaBytes), c.Path, null))
            .ToList();
        return Chart("recent-growth-timeline", "Recent growth timeline", "Time", "Snapshot changes in detection order.", VisualChartKind.Timeline, points, insight: "Growth spikes connect user-visible pressure to recent tools or updates.");
    }

    private static ChartDefinition BuildDeltaWaterfall(IReadOnlyList<ChangeRecord> changes)
    {
        var points = changes
            .OrderByDescending(c => Math.Abs(c.DeltaBytes))
            .Take(12)
            .Select(c => Point(ChangePathName(c), c.PreviousSizeBytes, c.DeltaBytes, c.DeltaBytes >= 0 ? "Danger" : "Safe", FormatSignedBytes(c.DeltaBytes), c.Path, null))
            .ToList();
        return Chart("snapshot-delta-waterfall", "Snapshot delta waterfall", "Time", "Largest growth and shrink events since the previous scan.", VisualChartKind.Waterfall, points, insight: "Waterfalls separate real growth from cleanup wins.");
    }

    private static ChartDefinition BuildReclaimableFunnel(IReadOnlyList<CleanupFinding> findings)
    {
        var points = findings
            .GroupBy(f => f.Safety)
            .Select(g => Point(DisplaySafety(g.Key), g.Sum(f => f.SizeBytes), g.Count(), SafetyColor(g.Key), FormatBytes(g.Sum(f => f.SizeBytes)), "", null))
            .OrderByDescending(p => p.SizeBytes)
            .ToList();
        return Chart("reclaimable-safety-funnel", "Reclaimable safety funnel", "Cleanup", "Cleanup potential grouped by guardrail lane.", VisualChartKind.Funnel, points, insight: "Safe is actionable, Review needs human context, system lanes stay guarded.");
    }

    private static ChartDefinition BuildCleanupEffortValue(IReadOnlyList<CleanupFinding> findings)
    {
        var points = findings
            .Select(f =>
            {
                var effort = f.Safety switch
                {
                    CleanupSafety.Safe => 0.25,
                    CleanupSafety.Review => 0.58,
                    CleanupSafety.UseSystemCleanup => 0.78,
                    _ => 0.95
                };
                return Point(f.DisplayName, effort, Math.Log10(Math.Max(1, f.SizeBytes / 1024d / 1024d)), SafetyColor(f.Safety), FormatBytes(f.SizeBytes), f.Path, f.NodeId, f.Confidence);
            })
            .ToList();
        return Chart("cleanup-effort-value", "Cleanup effort/value matrix", "Cleanup", "Bigger reclaim vs review effort.", VisualChartKind.Scatter, points, insight: "Best candidates sit high and left: large, safe, and backed by strong evidence.");
    }

    private static ChartDefinition BuildSafetyStack(IReadOnlyList<CleanupFinding> findings)
    {
        var series = findings
            .GroupBy(f => f.Category)
            .Select(g => new ChartSeries
            {
                Name = g.Key,
                ColorKey = g.Key,
                Points = g.GroupBy(f => DisplaySafety(f.Safety))
                    .Select(s => Point(s.Key, s.Sum(f => f.SizeBytes), s.Count(), SafetyColor(s.First().Safety), FormatBytes(s.Sum(f => f.SizeBytes)), "", null))
                    .ToList()
            })
            .Take(8)
            .ToList();
        return Chart("safe-review-stacked-bars", "Safe vs review stacked bars", "Cleanup", "Cleanup lanes by finding category.", VisualChartKind.StackedBar, series: series, insight: "Developer artifacts often split between safe generated caches and review-heavy dependencies.");
    }

    private static ChartDefinition BuildBlockedRiskMap(IReadOnlyList<CleanupFinding> findings)
    {
        var cells = findings
            .GroupBy(f => new { Safety = DisplaySafety(f.Safety), f.Category })
            .Select(g => Cell(g.Key.Safety, g.Key.Category, g.Sum(f => f.SizeBytes), g.Count(), SafetyColor(g.First().Safety)))
            .ToList();
        return Chart("blocked-system-risk-map", "Blocked/system risk map", "Cleanup", "Where cleanup guardrails prevent unsafe actions.", VisualChartKind.Matrix, cells: cells, insight: "Blocked cells are explanations, not action buttons.");
    }

    private static ChartDefinition BuildOwnershipFlow(IReadOnlyList<StorageRelationship> relationships, IReadOnlyList<FileSystemNode> nodes)
    {
        var nodeSize = nodes.ToDictionary(n => n.Id, SizeOf);
        var flows = relationships
            .Where(r => !string.IsNullOrWhiteSpace(r.Owner))
            .Select(r => Flow(r.Owner, ShortPath(r.SourcePath), nodeSize.GetValueOrDefault(r.SourceNodeId), r.Evidence.Confidence, r.Kind.ToString(), r.SourceNodeId, r.Label))
            .OrderByDescending(f => f.SizeBytes)
            .Take(18)
            .ToList();
        return Chart("app-ownership-sankey", "App ownership Sankey", "Ownership", "Evidence-backed owners linked to storage roots.", VisualChartKind.RelationshipFlow, flows: flows, insight: "Ownership turns unknown folders into app-specific decisions.");
    }

    private static ChartDefinition BuildCacheOwnershipMatrix(IReadOnlyList<StorageRelationship> relationships, IReadOnlyList<CleanupFinding> findings)
    {
        var findingByPath = findings.ToDictionary(f => f.Path, f => f, IgnoreCase);
        var cells = relationships
            .Where(r => r.Kind is FileSystemEdgeKind.CacheOwnership or FileSystemEdgeKind.AppOwnership)
            .Select(r =>
            {
                findingByPath.TryGetValue(r.SourcePath, out var finding);
                return Cell(r.Owner, r.Label, finding?.SizeBytes ?? 1, 1, finding is null ? "Link" : SafetyColor(finding.Safety));
            })
            .ToList();
        return Chart("cache-ownership-matrix", "Cache ownership matrix", "Ownership", "Owners crossed with relationship type.", VisualChartKind.Matrix, cells: cells, insight: "Cache ownership helps distinguish disposable data from active state.");
    }

    private static ChartDefinition BuildDeveloperDependencyFlow(IReadOnlyList<StorageRelationship> relationships, IReadOnlyList<FileSystemNode> nodes)
    {
        var nodeSize = nodes.ToDictionary(n => n.Id, SizeOf);
        var flows = relationships
            .Where(r => r.Kind == FileSystemEdgeKind.PackageArtifact || IsDeveloperPath(r.SourcePath))
            .Select(r => Flow(ShortPath(r.TargetPath ?? r.Owner), ShortPath(r.SourcePath), nodeSize.GetValueOrDefault(r.SourceNodeId), r.Evidence.Confidence, "Code", r.SourceNodeId, r.Evidence.Detail))
            .OrderByDescending(f => f.SizeBytes)
            .Take(18)
            .ToList();
        return Chart("dev-artifact-dependency-graph", "Dev artifact dependency graph", "Ownership", "Generated folders connected to package manifests and build tools.", VisualChartKind.RelationshipFlow, flows: flows, insight: "Generated dependencies are removable only when the project can reinstall them.");
    }

    private static ChartDefinition BuildCloudLocalStatus(IReadOnlyList<FileSystemNode> nodes, IReadOnlyList<StorageRelationship> relationships)
    {
        var cloudPaths = relationships
            .Where(r => r.Owner.Contains("OneDrive", StringComparison.OrdinalIgnoreCase) || r.SourcePath.Contains("OneDrive", StringComparison.OrdinalIgnoreCase))
            .Select(r => r.SourcePath)
            .ToHashSet(IgnoreCase);
        var localBytes = nodes.Where(n => n.Kind == FileSystemNodeKind.File && !cloudPaths.Any(path => n.FullPath.StartsWith(path, StringComparison.OrdinalIgnoreCase))).Sum(SizeOf);
        var cloudBytes = nodes.Where(n => cloudPaths.Any(path => n.FullPath.StartsWith(path, StringComparison.OrdinalIgnoreCase))).Sum(SizeOf);
        var points = new[]
        {
            Point("Local-only", localBytes, 1, "Drive", FormatBytes(localBytes), "", null),
            Point("Cloud-linked", cloudBytes, 1, "Link", FormatBytes(cloudBytes), "", null)
        };
        return Chart("cloud-local-status-bars", "Cloud/local status bars", "Ownership", "Separates local-only bytes from cloud-linked roots.", VisualChartKind.StackedBar, points, insight: "Cloud-linked storage needs sync risk warnings before cleanup.");
    }

    private static ChartDefinition BuildPackageManagerFootprint(IReadOnlyList<FileSystemNode> nodes, IReadOnlyList<CleanupFinding> findings)
    {
        var candidates = nodes
            .Where(n => IsPackagePath(n.FullPath) || findings.Any(f => f.NodeId == n.Id && f.Category.Contains("Developer", StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(SizeOf)
            .Take(12)
            .Select(n => Point(n.Name, SizeOf(n), n.FileCount, "Code", FormatBytes(SizeOf(n)), n.FullPath, n.Id))
            .ToList();
        return Chart("package-manager-footprint", "Package-manager footprint", "System/dev", "npm, NuGet, Gradle, Python, Docker, and similar storage roots.", VisualChartKind.RankBar, candidates, insight: "Package caches are high-yield but can slow the next restore/build.");
    }

    private static ChartDefinition BuildSystemDevRoots(IReadOnlyList<FileSystemNode> nodes)
    {
        var points = nodes
            .Where(n => IsSystemDevRoot(n.FullPath))
            .OrderByDescending(SizeOf)
            .Take(12)
            .Select(n => Point(n.Name, SizeOf(n), n.FileCount, n.Category, FormatBytes(SizeOf(n)), n.FullPath, n.Id))
            .ToList();
        return Chart("docker-wsl-storage-roots", "Docker/WSL/storage roots", "System/dev", "Heavy virtualized or system-managed developer roots.", VisualChartKind.BubblePack, points, insight: "Virtual disks and image stores can dominate space but require tool-aware cleanup.");
    }

    private static ChartDefinition BuildDependencyCacheRadar(IReadOnlyList<FileSystemNode> nodes)
    {
        var categories = new[] { "node_modules", ".gradle", ".nuget", ".venv", "docker", "cache" };
        var points = categories
            .Select(name =>
            {
                var matches = nodes.Where(n => n.FullPath.Contains(name, StringComparison.OrdinalIgnoreCase)).ToList();
                var size = matches.Sum(SizeOf);
                return Point(name, Math.Max(0, size), Math.Min(1, Math.Log10(Math.Max(1, size / 1024d / 1024d)) / 5d), "Code", FormatBytes(size), "", matches.FirstOrDefault()?.Id);
            })
            .ToList();
        return Chart("dependency-cache-comparison", "Dependency-cache comparison", "System/dev", "Developer cache families normalized onto one radar.", VisualChartKind.Radar, points, insight: "Radar spikes show which ecosystem is responsible for repeated build storage.");
    }

    private static ChartDefinition BuildInstallerRiskRadar(IReadOnlyList<CleanupFinding> findings, IReadOnlyList<FileSystemNode> nodes)
    {
        var axes = new[]
        {
            ("Package cache", "Package Cache"),
            ("Installers", ".exe"),
            ("Windows update", "SoftwareDistribution"),
            ("Component store", "WinSxS"),
            ("Driver/vendor", "NVIDIA")
        };
        var points = axes.Select(axis =>
        {
            var findingSize = findings.Where(f => f.Path.Contains(axis.Item2, StringComparison.OrdinalIgnoreCase)).Sum(f => f.SizeBytes);
            var nodeSize = nodes.Where(n => n.FullPath.Contains(axis.Item2, StringComparison.OrdinalIgnoreCase) || string.Equals(n.Extension, axis.Item2, StringComparison.OrdinalIgnoreCase)).Sum(SizeOf);
            var size = Math.Max(findingSize, nodeSize);
            return Point(axis.Item1, size, Math.Min(1, Math.Log10(Math.Max(1, size / 1024d / 1024d)) / 5d), "Installer", FormatBytes(size), "", null);
        }).ToList();
        return Chart("installer-cache-risk-radar", "Installer/cache risk radar", "System/dev", "System and installer storage areas normalized by risk and size.", VisualChartKind.Radar, points, insight: "Installer and system caches are usually review or official-cleanup lanes.");
    }

    private static IReadOnlyList<TutorialStep> BuildTutorials()
    {
        return
        [
            Tutorial("download", "Download and first run", "Try the app without building it", "Use the installer or portable ZIP from GitHub Releases. The first-run screen can open demo data without scanning private drives.", "Click Open demo workspace for a safe tour, or Scan this PC for local results.", "Preview builds are unsigned and no telemetry is collected."),
            Tutorial("first-scan", "Run a first scan", "Get useful results fast", "Pick a drive and use Scan selected. Disk Space Inspector records permission gaps instead of hiding them.", "Open Overview and watch accounted bytes, throughput, and scan gaps.", "Scanning is read-only."),
            Tutorial("visual-lab", "Use Visual Lab", "Find the strongest storage story", "Start with Best insights, then expand Advanced algorithms when you need a deeper explanation.", "Click a chart item to sync the selected path when the chart has a node-backed point.", "Charts explain; cleanup still happens through review lanes."),
            Tutorial("cleanup", "Stage cleanup safely", "Separate Safe, Review, System, and Blocked", "Safe means staged for review, Review means inspect ownership, System means use official Windows/app cleanup, Blocked means leave alone.", "Use Cleanup and inspect evidence before staging.", "No direct destructive action is performed in this version."),
            Tutorial("codex", "Ask Codex AI", "Rank cleanup candidates with your ChatGPT login", "Use Login with Codex, Check Codex status, then Ask Codex AI. The app never reads Codex token files.", "Review AI suggestions next to app guardrails.", "AI cannot invent paths or make blocked items executable."),
            Tutorial("relationships", "Read relationships", "Understand why items are connected", "Relationships show evidence like cache ownership, package manifests, cloud sync roots, and generated artifacts.", "Open Insights or select a chart item with relationship evidence.", "Low-confidence edges should guide review, not deletion."),
            Tutorial("demo", "Use demo mode", "Show the product without scanning private data", "Launch with --demo to seed realistic non-personal drives, charts, findings, relationships, and tutorials.", "Use demo mode for screenshots and walkthroughs.", "Demo paths are fake and safe to publish.")
        ];
    }

    private static ChartDefinition Chart(
        string key,
        string title,
        string group,
        string description,
        VisualChartKind kind,
        IReadOnlyList<ChartPoint>? points = null,
        IReadOnlyList<HeatmapCell>? cells = null,
        IReadOnlyList<RelationshipFlow>? flows = null,
        IReadOnlyList<ChartSeries>? series = null,
        IReadOnlyList<ChartMetric>? metrics = null,
        string insight = "",
        bool advanced = false)
    {
        return new ChartDefinition
        {
            Key = key,
            Title = title,
            Group = group,
            Description = description,
            Kind = kind,
            Points = points ?? [],
            Cells = cells ?? [],
            Flows = flows ?? [],
            Series = series ?? [],
            Metrics = metrics ?? [],
            Insight = insight,
            IsAdvanced = advanced || group is "Ownership" or "System/dev"
        };
    }

    private static ChartMetric Metric(string label, string value, string detail, string colorKey)
    {
        return new ChartMetric { Label = label, Value = value, Detail = detail, ColorKey = colorKey };
    }

    private static ChartPoint Point(string label, double x, double y, string colorKey, string display, string path, long? nodeId, double score = 0)
    {
        var size = x > 0 ? (long)Math.Min(long.MaxValue, x) : 0;
        return new ChartPoint
        {
            Label = string.IsNullOrWhiteSpace(label) ? "(unknown)" : label,
            X = x,
            Y = y,
            Score = score,
            Fraction = score is >= 0 and <= 1 ? score : 0,
            SizeBytes = size,
            Count = y > 0 && y < int.MaxValue ? (int)y : 0,
            ColorKey = colorKey,
            Category = colorKey,
            DisplayValue = display,
            Path = path,
            NodeId = nodeId,
            Detail = string.IsNullOrWhiteSpace(path) ? display : $"{display} - {path}"
        };
    }

    private static HeatmapCell Cell(string row, string column, long sizeBytes, int count, string colorKey)
    {
        return new HeatmapCell
        {
            Row = row,
            Column = column,
            Label = $"{row} / {column}",
            Value = Math.Max(1, sizeBytes),
            SizeBytes = sizeBytes,
            Count = count,
            ColorKey = colorKey,
            Detail = $"{FormatBytes(sizeBytes)}, {count:n0} item(s)"
        };
    }

    private static RelationshipFlow Flow(string source, string target, long sizeBytes, double confidence, string colorKey, long? nodeId, string detail)
    {
        return new RelationshipFlow
        {
            Source = string.IsNullOrWhiteSpace(source) ? "(unknown)" : source,
            Target = string.IsNullOrWhiteSpace(target) ? "(unknown)" : target,
            Label = $"{source} -> {target}",
            Value = Math.Max(1, sizeBytes),
            SizeBytes = sizeBytes,
            Confidence = confidence,
            ColorKey = colorKey,
            Detail = detail,
            NodeId = nodeId
        };
    }

    private static TutorialStep Tutorial(string key, string title, string goal, string body, string action, string safety)
    {
        return new TutorialStep { Key = key, Title = title, Goal = goal, Body = body, Action = action, SafetyNote = safety };
    }

    private static IReadOnlyList<ChartPoint> GroupByCategory(IReadOnlyList<FileSystemNode> nodes, Func<FileSystemNode, long> value)
    {
        return nodes
            .Where(n => value(n) > 0)
            .GroupBy(n => CleanCategory(n.Category))
            .Select(g => Point(g.Key, g.Sum(value), g.Count(), g.Key, FormatBytes(g.Sum(value)), "", null))
            .OrderByDescending(p => p.SizeBytes)
            .Take(10)
            .ToList();
    }

    private static string ChangePathName(ChangeRecord change)
    {
        return ShortPath(change.Path);
    }

    private static string ShortPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "(unknown)";
        }

        var trimmed = path.TrimEnd('\\', '/');
        var index = trimmed.LastIndexOfAny(['\\', '/']);
        return index >= 0 && index + 1 < trimmed.Length ? trimmed[(index + 1)..] : trimmed;
    }

    private static string CleanCategory(string? category)
    {
        return string.IsNullOrWhiteSpace(category) ? "Other" : category;
    }

    private static string ExtensionCategory(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".mov" or ".mp4" or ".mkv" => "Video",
            ".jpg" or ".jpeg" or ".png" or ".raw" => "Image",
            ".zip" or ".7z" or ".iso" or ".bak" or ".vhdx" => "Archive",
            ".exe" or ".msi" => "Installer",
            ".sys" => "System",
            ".js" or ".ts" or ".dll" or ".py" => "Code",
            _ => "Other"
        };
    }

    private static string DisplaySafety(CleanupSafety safety)
    {
        return safety == CleanupSafety.UseSystemCleanup ? "Use system cleanup" : safety.ToString();
    }

    private static string SafetyColor(CleanupSafety safety)
    {
        return safety switch
        {
            CleanupSafety.Safe => "Safe",
            CleanupSafety.Review => "Review",
            CleanupSafety.UseSystemCleanup => "System",
            CleanupSafety.Blocked => "Blocked",
            _ => "Other"
        };
    }

    private static string AgeBucket(DateTimeOffset now, DateTimeOffset modified)
    {
        var days = (now - modified).TotalDays;
        return days <= 30 ? "Last 30 days" :
            days <= 90 ? "30-90 days" :
            days <= 365 ? "90 days-1 year" :
            days <= 1095 ? "1-3 years" : "3+ years";
    }

    private static int AgeBucketIndex(string bucket)
    {
        return bucket switch
        {
            "Last 30 days" => 1,
            "30-90 days" => 2,
            "90 days-1 year" => 3,
            "1-3 years" => 4,
            _ => 5
        };
    }

    private static int ISOWeekOfYear(DateTime date)
    {
        return System.Globalization.ISOWeek.GetWeekOfYear(date);
    }

    private static bool IsDeveloperPath(string path)
    {
        return path.Contains("node_modules", StringComparison.OrdinalIgnoreCase) ||
               path.Contains(".next", StringComparison.OrdinalIgnoreCase) ||
               path.Contains(".gradle", StringComparison.OrdinalIgnoreCase) ||
               path.Contains(".venv", StringComparison.OrdinalIgnoreCase) ||
               path.Contains(".nuget", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPackagePath(string path)
    {
        return IsDeveloperPath(path) ||
               path.Contains("Package Cache", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("Docker", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSystemDevRoot(string path)
    {
        return path.Contains("Docker", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("WSL", StringComparison.OrdinalIgnoreCase) ||
               path.Contains(".vhdx", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("SoftwareDistribution", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("WinSxS", StringComparison.OrdinalIgnoreCase);
    }

    private static long SizeOf(FileSystemNode node)
    {
        return Math.Max(node.TotalPhysicalLength, Math.Max(node.PhysicalLength, node.TotalLength));
    }

    private static string FormatSignedBytes(long bytes)
    {
        return bytes >= 0 ? $"+{FormatBytes(bytes)}" : $"-{FormatBytes(Math.Abs(bytes))}";
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = Math.Abs((double)bytes);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        var sign = bytes < 0 ? "-" : string.Empty;
        return unit == 0 ? $"{sign}{value:n0} {units[unit]}" : $"{sign}{value:n1} {units[unit]}";
    }
}
