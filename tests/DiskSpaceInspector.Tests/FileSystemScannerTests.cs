using System.Runtime.InteropServices;
using DiskSpaceInspector.Core.Models;
using DiskSpaceInspector.Core.Scanning;
using DiskSpaceInspector.Core.Windows;

namespace DiskSpaceInspector.Tests;

[TestClass]
public sealed class FileSystemScannerTests
{
    [TestMethod]
    public async Task ScanAsync_AggregatesNestedLogicalSizes()
    {
        using var fixture = new TempDirectory();
        File.WriteAllBytes(Path.Combine(fixture.Path, "root.bin"), new byte[100]);
        Directory.CreateDirectory(Path.Combine(fixture.Path, "child"));
        File.WriteAllBytes(Path.Combine(fixture.Path, "child", "nested.bin"), new byte[50]);

        var result = await ScanAsync(fixture.Path);
        var root = result.Nodes.Single(n => n.ParentId is null);

        Assert.AreEqual(ScanStatus.Completed, result.Session.Status);
        Assert.AreEqual(150, root.TotalLength);
        Assert.AreEqual(2, root.FileCount);
        Assert.IsTrue(root.FolderCount >= 1);
    }

    [TestMethod]
    public async Task ScanAsync_DoesNotFollowDirectorySymlinks()
    {
        using var fixture = new TempDirectory();
        var target = Path.Combine(fixture.Path, "target");
        var link = Path.Combine(fixture.Path, "target-link");
        Directory.CreateDirectory(target);
        File.WriteAllBytes(Path.Combine(target, "inside.bin"), new byte[25]);

        try
        {
            Directory.CreateSymbolicLink(link, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Assert.Inconclusive("Creating directory symlinks requires developer mode or elevation on some Windows configurations.");
        }

        var result = await ScanAsync(fixture.Path);

        Assert.AreEqual(25, result.Nodes.Single(n => n.ParentId is null).TotalLength);
        Assert.IsTrue(result.Nodes.Any(n => n.IsReparsePoint && n.Name == "target-link"));
        Assert.IsTrue(result.Edges.Any(e => e.Kind is FileSystemEdgeKind.JunctionTarget or FileSystemEdgeKind.SymlinkTarget));
    }

    [TestMethod]
    public async Task ScanAsync_DoesNotDoubleCountHardlinkedPhysicalSize()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Hardlink test is Windows-specific.");
        }

        using var fixture = new TempDirectory();
        var original = Path.Combine(fixture.Path, "original.bin");
        var linked = Path.Combine(fixture.Path, "linked.bin");
        File.WriteAllBytes(original, new byte[4096]);

        if (!CreateHardLinkW(linked, original, IntPtr.Zero))
        {
            Assert.Inconclusive("Could not create a hardlink in this filesystem.");
        }

        var result = await ScanAsync(fixture.Path);

        Assert.AreEqual(8192, result.Nodes.Single(n => n.ParentId is null).TotalLength);
        Assert.IsTrue(result.Nodes.Any(n => n.IsHardLinkDuplicate));
        Assert.IsTrue(result.Edges.Any(e => e.Kind == FileSystemEdgeKind.HardlinkSibling));
    }

    [TestMethod]
    public async Task ScanAsync_ReturnsCancelledSessionWhenTokenIsCancelled()
    {
        using var fixture = new TempDirectory();
        File.WriteAllBytes(Path.Combine(fixture.Path, "root.bin"), new byte[100]);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var result = await ScanAsync(fixture.Path, cts.Token);

        Assert.AreEqual(ScanStatus.Cancelled, result.Session.Status);
    }

    [TestMethod]
    public async Task FastFirstScan_DefersRelationshipResolution()
    {
        using var fixture = new TempDirectory();
        File.WriteAllBytes(Path.Combine(fixture.Path, "root.bin"), new byte[100]);
        var resolver = new CountingRelationshipResolver();
        var scanner = new FileSystemScanner(resolver);

        var result = await scanner.ScanAsync(
            new ScanRequest(new VolumeInfo
            {
                Name = "Fixture",
                RootPath = fixture.Path,
                DriveType = "Fixed",
                IsReady = true
            })
            {
                MaxConcurrency = 1,
                PipelineOptions = ScanPipelineOptions.FastFirstScan
            });

        Assert.AreEqual(ScanStatus.Completed, result.Session.Status);
        Assert.AreEqual(0, resolver.ResolveCallCount);
    }

    private static Task<ScanResult> ScanAsync(string rootPath, CancellationToken cancellationToken = default)
    {
        var scanner = new FileSystemScanner(new WindowsRelationshipResolver());
        return scanner.ScanAsync(
            new ScanRequest(new VolumeInfo
            {
                Name = "Fixture",
                RootPath = rootPath,
                DriveType = "Fixed",
                IsReady = true
            })
            {
                MaxConcurrency = 2
            },
            cancellationToken: cancellationToken);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLinkW(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);

    private sealed class CountingRelationshipResolver : DiskSpaceInspector.Core.Services.IRelationshipResolver
    {
        public int ResolveCallCount { get; private set; }

        public IReadOnlyList<FileSystemEdge> Resolve(FileSystemNode node)
        {
            ResolveCallCount++;
            return [];
        }
    }
}
