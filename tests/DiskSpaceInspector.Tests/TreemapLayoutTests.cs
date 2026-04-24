using DiskSpaceInspector.Core.Layout;
using DiskSpaceInspector.Core.Models;

namespace DiskSpaceInspector.Tests;

[TestClass]
public sealed class TreemapLayoutTests
{
    [TestMethod]
    public void Layout_IsDeterministicAndWithinBounds()
    {
        var service = new SliceTreemapLayoutService();
        var parent = Node(1, "root", 100);
        var children = new[]
        {
            Node(2, "alpha", 60),
            Node(3, "beta", 30),
            Node(4, "gamma", 10)
        };

        var first = service.Layout(parent, children, new TreemapBounds(0, 0, 100, 50));
        var second = service.Layout(parent, children, new TreemapBounds(0, 0, 100, 50));

        CollectionAssert.AreEqual(first.Select(r => r.Label).ToList(), second.Select(r => r.Label).ToList());
        Assert.AreEqual(3, first.Count);
        foreach (var rect in first)
        {
            Assert.IsTrue(rect.Bounds.X >= 0);
            Assert.IsTrue(rect.Bounds.Y >= 0);
            Assert.IsTrue(rect.Bounds.X + rect.Bounds.Width <= 100.0001);
            Assert.IsTrue(rect.Bounds.Y + rect.Bounds.Height <= 50.0001);
        }
    }

    [TestMethod]
    public void Layout_AggregatesSmallItemsWhenTileLimitIsExceeded()
    {
        var service = new SliceTreemapLayoutService();
        var parent = Node(1, "root", 100);
        var children = Enumerable.Range(0, 10).Select(i => Node(i + 2, $"file-{i}", 10)).ToList();

        var rectangles = service.Layout(parent, children, new TreemapBounds(0, 0, 100, 50), maxTiles: 5);

        Assert.AreEqual(5, rectangles.Count);
        Assert.IsTrue(rectangles.Any(r => r.Label == "Other small items"));
    }

    [TestMethod]
    public void SquarifiedLayout_PreservesAreaProportions()
    {
        var service = new SquarifiedTreemapLayoutService();
        var parent = Node(1, "root", 100);
        var children = new[]
        {
            Node(2, "alpha", 75),
            Node(3, "beta", 25)
        };

        var rectangles = service.Layout(parent, children, new TreemapBounds(0, 0, 100, 100));
        var alpha = rectangles.Single(r => r.Label == "alpha");
        var beta = rectangles.Single(r => r.Label == "beta");
        var alphaArea = alpha.Bounds.Width * alpha.Bounds.Height;
        var betaArea = beta.Bounds.Width * beta.Bounds.Height;

        Assert.AreEqual(3, alphaArea / betaArea, 0.15);
    }

    [TestMethod]
    public void SunburstLayout_IsDeterministicAndWithinRings()
    {
        var service = new SunburstLayoutService();
        var root = Node(1, "root", 100);
        var childA = Node(2, "alpha", 60);
        childA.ParentId = 1;
        var childB = Node(3, "beta", 40);
        childB.ParentId = 1;
        var grandchild = Node(4, "nested", 30);
        grandchild.ParentId = 2;
        var children = new Dictionary<long, List<FileSystemNode>>
        {
            [1] = [childA, childB],
            [2] = [grandchild]
        };

        var first = service.Layout(root, children, maxDepth: 3);
        var second = service.Layout(root, children, maxDepth: 3);

        CollectionAssert.AreEqual(first.Select(s => s.Label).ToList(), second.Select(s => s.Label).ToList());
        Assert.IsTrue(first.Count >= 3);
        Assert.IsTrue(first.All(s => s.InnerRadius >= 0 && s.OuterRadius <= 1.0001));
        Assert.AreEqual(360, first.Where(s => s.Depth == 1).Sum(s => s.SweepAngle), 0.001);
    }

    private static FileSystemNode Node(long id, string name, long size)
    {
        return new FileSystemNode
        {
            Id = id,
            Name = name,
            FullPath = name,
            Kind = FileSystemNodeKind.Directory,
            TotalPhysicalLength = size,
            TotalLength = size,
            Category = "Folder"
        };
    }
}
