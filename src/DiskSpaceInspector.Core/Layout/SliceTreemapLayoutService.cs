using DiskSpaceInspector.Core.Models;
using DiskSpaceInspector.Core.Services;

namespace DiskSpaceInspector.Core.Layout;

public sealed class SliceTreemapLayoutService : ITreemapLayoutService
{
    private readonly SquarifiedTreemapLayoutService _squarified = new();

    public IReadOnlyList<TreemapRectangle> Layout(
        FileSystemNode parent,
        IReadOnlyList<FileSystemNode> children,
        TreemapBounds bounds,
        int maxTiles = 180)
    {
        return _squarified.Layout(parent, children, bounds, maxTiles);
    }

    public IReadOnlyList<TreemapRectangle> LegacySliceLayout(
        FileSystemNode parent,
        IReadOnlyList<FileSystemNode> children,
        TreemapBounds bounds,
        int maxTiles = 180)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return [];
        }

        var visible = children
            .Where(c => EffectiveSize(c) > 0)
            .OrderByDescending(EffectiveSize)
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (visible.Count == 0)
        {
            return [];
        }

        if (visible.Count > maxTiles)
        {
            var kept = visible.Take(maxTiles - 1).ToList();
            var remaining = visible.Skip(maxTiles - 1).ToList();
            kept.Add(new FileSystemNode
            {
                Id = -1,
                ParentId = parent.Id,
                Name = "Other small items",
                FullPath = parent.FullPath,
                Kind = FileSystemNodeKind.Directory,
                TotalPhysicalLength = remaining.Sum(EffectiveSize),
                TotalLength = remaining.Sum(c => c.TotalLength),
                Category = "Other"
            });
            visible = kept;
        }

        var output = new List<TreemapRectangle>(visible.Count);
        Slice(visible, bounds, output);
        return output;
    }

    private static void Slice(
        IReadOnlyList<FileSystemNode> nodes,
        TreemapBounds bounds,
        ICollection<TreemapRectangle> output)
    {
        var total = nodes.Sum(EffectiveSize);
        if (total <= 0)
        {
            return;
        }

        var offset = 0d;
        var horizontal = bounds.Width >= bounds.Height;

        foreach (var node in nodes)
        {
            var fraction = EffectiveSize(node) / (double)total;
            var width = horizontal ? bounds.Width * fraction : bounds.Width;
            var height = horizontal ? bounds.Height : bounds.Height * fraction;
            var x = horizontal ? bounds.X + offset : bounds.X;
            var y = horizontal ? bounds.Y : bounds.Y + offset;

            output.Add(new TreemapRectangle
            {
                NodeId = node.Id > 0 ? node.Id : null,
                Label = node.Name,
                Path = node.FullPath,
                SizeBytes = EffectiveSize(node),
                Bounds = new TreemapBounds(x, y, Math.Max(0, width), Math.Max(0, height)),
                ColorKey = node.Category
            });

            offset += horizontal ? width : height;
        }
    }

    private static long EffectiveSize(FileSystemNode node)
    {
        return Math.Max(node.TotalPhysicalLength, Math.Max(node.TotalLength, node.PhysicalLength));
    }
}
