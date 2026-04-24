using DiskSpaceInspector.Core.Cleanup;
using DiskSpaceInspector.Core.Models;

namespace DiskSpaceInspector.Tests;

[TestClass]
public sealed class CleanupClassifierTests
{
    private readonly CleanupClassifier _classifier = new();

    [TestMethod]
    public void Classify_BlocksWindowsInstaller()
    {
        var finding = _classifier.Classify(Node(@"C:\Windows\Installer\cached.msi", "cached.msi", FileSystemNodeKind.File));

        Assert.IsNotNull(finding);
        Assert.AreEqual(CleanupSafety.Blocked, finding.Safety);
        Assert.AreEqual(CleanupActionKind.LeaveAlone, finding.RecommendedAction);
    }

    [TestMethod]
    public void Classify_BlocksWinSxS()
    {
        var finding = _classifier.Classify(Node(@"C:\Windows\WinSxS\amd64_component", "amd64_component", FileSystemNodeKind.Directory));

        Assert.IsNotNull(finding);
        Assert.AreEqual(CleanupSafety.Blocked, finding.Safety);
    }

    [TestMethod]
    public void Classify_DoesNotTreatBrowserStateAsCache()
    {
        var finding = _classifier.Classify(Node(
            @"C:\Users\andre\AppData\Local\Google\Chrome\User Data\Default\Login Data",
            "Login Data",
            FileSystemNodeKind.File));

        Assert.IsNotNull(finding);
        Assert.AreEqual(CleanupSafety.Blocked, finding.Safety);
        StringAssert.Contains(finding.Explanation, "state");
    }

    [TestMethod]
    public void Classify_RecognizesTempAsSafeCacheClear()
    {
        var finding = _classifier.Classify(Node(
            Path.Combine(Path.GetTempPath(), "DiskSpaceInspectorTest", "cache.bin"),
            "cache.bin",
            FileSystemNodeKind.File));

        Assert.IsNotNull(finding);
        Assert.AreEqual(CleanupSafety.Safe, finding.Safety);
        Assert.AreEqual(CleanupActionKind.ClearCache, finding.RecommendedAction);
    }

    [TestMethod]
    public void CleanupPlanBuilder_ExcludesBlockedAndSystemCleanupFindings()
    {
        var builder = new CleanupPlanBuilder();
        var safe = _classifier.Classify(Node(Path.Combine(Path.GetTempPath(), "safe.tmp"), "safe.tmp", FileSystemNodeKind.File))!;
        var blocked = _classifier.Classify(Node(@"C:\Windows\System32\drivers\etc\hosts", "hosts", FileSystemNodeKind.File))!;
        var system = _classifier.Classify(Node(@"C:\Windows\SoftwareDistribution\Download", "Download", FileSystemNodeKind.Directory))!;

        var plan = builder.Build([safe, blocked, system]);

        Assert.AreEqual(1, plan.Findings.Count);
        Assert.AreEqual(safe.Id, plan.Findings[0].Id);
        Assert.AreEqual(1, plan.BlockedCount);
        Assert.AreEqual(1, plan.SystemCleanupCount);
    }

    private static FileSystemNode Node(string path, string name, FileSystemNodeKind kind)
    {
        return new FileSystemNode
        {
            Id = 10,
            Name = name,
            FullPath = path,
            Kind = kind,
            Length = 128 * 1024 * 1024,
            PhysicalLength = 128 * 1024 * 1024,
            TotalLength = 128 * 1024 * 1024,
            TotalPhysicalLength = 128 * 1024 * 1024,
            FileCount = kind == FileSystemNodeKind.File ? 1 : 20
        };
    }
}
