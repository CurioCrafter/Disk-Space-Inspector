using DiskSpaceInspector.Core.Models;
using DiskSpaceInspector.Core.Services;

namespace DiskSpaceInspector.Core.Layout;

public sealed class StorageBreakdownService : IStorageBreakdownService
{
    public IReadOnlyList<StorageBreakdownItem> BuildTypeBreakdown(IEnumerable<FileSystemNode> nodes, int maxItems = 12)
    {
        var files = nodes
            .Where(n => n.Kind == FileSystemNodeKind.File)
            .Where(n => EffectiveSize(n) > 0)
            .ToList();
        var total = files.Sum(EffectiveSize);
        if (total <= 0)
        {
            return [];
        }

        var groups = files
            .GroupBy(n => string.IsNullOrWhiteSpace(n.Category) ? "File" : n.Category, StringComparer.OrdinalIgnoreCase)
            .Select(g => new StorageBreakdownItem
            {
                Key = g.Key,
                Label = g.Key,
                SizeBytes = g.Sum(EffectiveSize),
                Count = g.Count(),
                Fraction = g.Sum(EffectiveSize) / (double)total,
                ColorKey = g.Key
            })
            .OrderByDescending(i => i.SizeBytes)
            .ThenBy(i => i.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return AggregateTail(groups, maxItems, total);
    }

    public IReadOnlyList<AgeHistogramBucket> BuildAgeHistogram(IEnumerable<FileSystemNode> nodes)
    {
        var now = DateTimeOffset.UtcNow;
        var buckets = new[]
        {
            new MutableAgeBucket("Last 30 days"),
            new MutableAgeBucket("30-90 days"),
            new MutableAgeBucket("90 days-1 year"),
            new MutableAgeBucket("1-3 years"),
            new MutableAgeBucket("3+ years"),
            new MutableAgeBucket("Unknown")
        };

        foreach (var node in nodes.Where(n => n.Kind == FileSystemNodeKind.File && EffectiveSize(n) > 0))
        {
            var age = node.LastModifiedUtc is { } modified ? now - modified : (TimeSpan?)null;
            var index = age is null ? 5 :
                age.Value.TotalDays <= 30 ? 0 :
                age.Value.TotalDays <= 90 ? 1 :
                age.Value.TotalDays <= 365 ? 2 :
                age.Value.TotalDays <= 365 * 3 ? 3 : 4;
            buckets[index].Add(EffectiveSize(node));
        }

        var total = buckets.Sum(b => b.SizeBytes);
        if (total <= 0)
        {
            return [];
        }

        return buckets.Select(b => new AgeHistogramBucket
        {
            Label = b.Label,
            SizeBytes = b.SizeBytes,
            Count = b.Count,
            Fraction = b.SizeBytes / (double)total
        }).ToList();
    }

    public IReadOnlyList<StorageBreakdownItem> BuildCleanupPotential(IEnumerable<CleanupFinding> findings)
    {
        var grouped = findings
            .GroupBy(f => f.Safety)
            .Select(g => new StorageBreakdownItem
            {
                Key = g.Key.ToString(),
                Label = DisplaySafety(g.Key),
                SizeBytes = g.Sum(f => f.SizeBytes),
                Count = g.Count(),
                ColorKey = g.Key.ToString()
            })
            .OrderByDescending(i => i.SizeBytes)
            .ToList();
        var total = grouped.Sum(i => i.SizeBytes);
        if (total <= 0)
        {
            return [];
        }

        return grouped.Select(i => new StorageBreakdownItem
        {
            Key = i.Key,
            Label = i.Label,
            SizeBytes = i.SizeBytes,
            Count = i.Count,
            Fraction = i.SizeBytes / (double)total,
            ColorKey = i.ColorKey
        }).ToList();
    }

    private static IReadOnlyList<StorageBreakdownItem> AggregateTail(
        IReadOnlyList<StorageBreakdownItem> groups,
        int maxItems,
        long total)
    {
        if (groups.Count <= maxItems)
        {
            return groups;
        }

        var kept = groups.Take(maxItems - 1).ToList();
        var tail = groups.Skip(maxItems - 1).ToList();
        kept.Add(new StorageBreakdownItem
        {
            Key = "Other",
            Label = "Other",
            SizeBytes = tail.Sum(i => i.SizeBytes),
            Count = tail.Sum(i => i.Count),
            Fraction = tail.Sum(i => i.SizeBytes) / (double)total,
            ColorKey = "Other"
        });
        return kept;
    }

    private static long EffectiveSize(FileSystemNode node)
    {
        return Math.Max(node.TotalPhysicalLength, Math.Max(node.TotalLength, node.PhysicalLength));
    }

    private static string DisplaySafety(CleanupSafety safety)
    {
        return safety switch
        {
            CleanupSafety.UseSystemCleanup => "Use system cleanup",
            _ => safety.ToString()
        };
    }

    private sealed class MutableAgeBucket
    {
        public MutableAgeBucket(string label)
        {
            Label = label;
        }

        public string Label { get; }

        public long SizeBytes { get; private set; }

        public int Count { get; private set; }

        public void Add(long sizeBytes)
        {
            SizeBytes += sizeBytes;
            Count++;
        }
    }
}
