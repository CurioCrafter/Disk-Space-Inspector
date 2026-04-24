using DiskSpaceInspector.Core.Models;
using DiskSpaceInspector.Core.Services;

namespace DiskSpaceInspector.Core.Layout;

public sealed class SquarifiedTreemapLayoutService : ISquarifiedTreemapLayoutService
{
    public IReadOnlyList<TreemapRectangle> Layout(
        FileSystemNode parent,
        IReadOnlyList<FileSystemNode> children,
        TreemapBounds bounds,
        int maxTiles = 240)
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

        var totalSize = visible.Sum(EffectiveSize);
        if (totalSize <= 0)
        {
            return [];
        }

        var area = bounds.Width * bounds.Height;
        var items = visible
            .Select(node => new LayoutItem(node, EffectiveSize(node) * area / totalSize))
            .ToList();
        var output = new List<TreemapRectangle>(items.Count);
        Squarify(items, bounds, output);
        return output;
    }

    private static void Squarify(List<LayoutItem> items, TreemapBounds bounds, ICollection<TreemapRectangle> output)
    {
        var remaining = new Queue<LayoutItem>(items);
        var row = new List<LayoutItem>();
        var free = bounds;

        while (remaining.Count > 0)
        {
            var item = remaining.Peek();
            var side = Math.Min(free.Width, free.Height);
            if (row.Count == 0 || Worst(row, side) >= Worst(row.Append(item), side))
            {
                row.Add(remaining.Dequeue());
                continue;
            }

            free = LayoutRow(row, free, output);
            row.Clear();
        }

        if (row.Count > 0)
        {
            LayoutRow(row, free, output);
        }
    }

    private static double Worst(IEnumerable<LayoutItem> row, double side)
    {
        var areas = row.Select(i => i.Area).Where(a => a > 0).ToList();
        if (areas.Count == 0 || side <= 0)
        {
            return double.MaxValue;
        }

        var sum = areas.Sum();
        var max = areas.Max();
        var min = areas.Min();
        var sideSquared = side * side;
        return Math.Max((sideSquared * max) / (sum * sum), (sum * sum) / (sideSquared * min));
    }

    private static TreemapBounds LayoutRow(
        IReadOnlyList<LayoutItem> row,
        TreemapBounds free,
        ICollection<TreemapRectangle> output)
    {
        var rowArea = row.Sum(i => i.Area);
        if (rowArea <= 0)
        {
            return free;
        }

        if (free.Width >= free.Height)
        {
            var rowWidth = Math.Min(free.Width, rowArea / Math.Max(1, free.Height));
            var y = free.Y;
            foreach (var item in row)
            {
                var height = item.Area / Math.Max(1, rowWidth);
                output.Add(ToRectangle(item.Node, new TreemapBounds(free.X, y, rowWidth, Math.Max(0, height))));
                y += height;
            }

            return new TreemapBounds(free.X + rowWidth, free.Y, Math.Max(0, free.Width - rowWidth), free.Height);
        }
        else
        {
            var rowHeight = Math.Min(free.Height, rowArea / Math.Max(1, free.Width));
            var x = free.X;
            foreach (var item in row)
            {
                var width = item.Area / Math.Max(1, rowHeight);
                output.Add(ToRectangle(item.Node, new TreemapBounds(x, free.Y, Math.Max(0, width), rowHeight)));
                x += width;
            }

            return new TreemapBounds(free.X, free.Y + rowHeight, free.Width, Math.Max(0, free.Height - rowHeight));
        }
    }

    private static TreemapRectangle ToRectangle(FileSystemNode node, TreemapBounds bounds)
    {
        return new TreemapRectangle
        {
            NodeId = node.Id > 0 ? node.Id : null,
            Label = node.Name,
            Path = node.FullPath,
            SizeBytes = EffectiveSize(node),
            Bounds = bounds,
            ColorKey = node.Category
        };
    }

    private static long EffectiveSize(FileSystemNode node)
    {
        return Math.Max(node.TotalPhysicalLength, Math.Max(node.TotalLength, node.PhysicalLength));
    }

    private readonly record struct LayoutItem(FileSystemNode Node, double Area);
}
