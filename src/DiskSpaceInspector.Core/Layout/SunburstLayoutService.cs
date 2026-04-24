using DiskSpaceInspector.Core.Models;
using DiskSpaceInspector.Core.Services;

namespace DiskSpaceInspector.Core.Layout;

public sealed class SunburstLayoutService : ISunburstLayoutService
{
    public IReadOnlyList<SunburstSegment> Layout(
        FileSystemNode root,
        IReadOnlyDictionary<long, List<FileSystemNode>> childrenByParent,
        int maxDepth = 4,
        int maxSegments = 320)
    {
        if (maxDepth <= 0 || maxSegments <= 0)
        {
            return [];
        }

        var output = new List<SunburstSegment>();
        var ringWidth = 1d / Math.Max(1, maxDepth);
        AddChildren(root, startAngle: -90, sweepAngle: 360, depth: 1);
        return output;

        void AddChildren(FileSystemNode parent, double startAngle, double sweepAngle, int depth)
        {
            if (depth > maxDepth || output.Count >= maxSegments)
            {
                return;
            }

            if (!childrenByParent.TryGetValue(parent.Id, out var children))
            {
                return;
            }

            var visible = children
                .Where(c => EffectiveSize(c) > 0)
                .OrderByDescending(EffectiveSize)
                .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .Take(Math.Max(1, maxSegments - output.Count))
                .ToList();
            var total = visible.Sum(EffectiveSize);
            if (total <= 0)
            {
                return;
            }

            var angle = startAngle;
            foreach (var child in visible)
            {
                var childSweep = sweepAngle * EffectiveSize(child) / total;
                if (childSweep <= 0.05)
                {
                    continue;
                }

                output.Add(new SunburstSegment
                {
                    NodeId = child.Id,
                    Label = child.Name,
                    Path = child.FullPath,
                    SizeBytes = EffectiveSize(child),
                    StartAngle = angle,
                    SweepAngle = childSweep,
                    InnerRadius = (depth - 1) * ringWidth,
                    OuterRadius = depth * ringWidth,
                    Depth = depth,
                    ColorKey = child.Category
                });

                AddChildren(child, angle, childSweep, depth + 1);
                angle += childSweep;
            }
        }
    }

    private static long EffectiveSize(FileSystemNode node)
    {
        return Math.Max(node.TotalPhysicalLength, Math.Max(node.TotalLength, node.PhysicalLength));
    }
}
