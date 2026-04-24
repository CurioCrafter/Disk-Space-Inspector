using System.Collections.ObjectModel;
using DiskSpaceInspector.App.Infrastructure;
using DiskSpaceInspector.Core.Models;

namespace DiskSpaceInspector.App.ViewModels;

public sealed class NodeTreeItemViewModel
{
    public NodeTreeItemViewModel(FileSystemNode node)
    {
        Node = node;
    }

    public FileSystemNode Node { get; }

    public long NodeId => Node.Id;

    public string Name => Node.Name;

    public string Size => ByteFormatter.Format(Math.Max(Node.TotalPhysicalLength, Node.PhysicalLength));

    public ObservableCollection<NodeTreeItemViewModel> Children { get; } = [];
}
