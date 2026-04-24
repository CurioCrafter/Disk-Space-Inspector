using DiskSpaceInspector.App.Infrastructure;
using DiskSpaceInspector.Core.Models;

namespace DiskSpaceInspector.App.ViewModels;

public sealed class NodeRowViewModel
{
    public NodeRowViewModel(FileSystemNode node, long parentSize, CleanupFinding? finding)
    {
        Node = node;
        Safety = finding is null ? string.Empty : DisplayText.ForSafety(finding.Safety);
        PercentOfParent = parentSize > 0 ? Math.Max(node.TotalPhysicalLength, node.PhysicalLength) / (double)parentSize : 0;
    }

    public FileSystemNode Node { get; }

    public long Id => Node.Id;

    public string Name => Node.Name;

    public string Path => Node.FullPath;

    public string Kind => Node.Kind.ToString();

    public string Category => Node.Category;

    public string Size => ByteFormatter.Format(Math.Max(Node.TotalPhysicalLength, Node.PhysicalLength));

    public long SizeBytes => Math.Max(Node.TotalPhysicalLength, Node.PhysicalLength);

    public string Percent => $"{PercentOfParent:P1}";

    public double PercentOfParent { get; }

    public string Items => Node.Kind == FileSystemNodeKind.File ? "1 file" : $"{Node.FileCount:n0} files / {Node.FolderCount:n0} folders";

    public string Modified => Node.LastModifiedUtc?.LocalDateTime.ToString("g") ?? "";

    public string Safety { get; }
}
