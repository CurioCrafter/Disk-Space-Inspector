namespace DiskSpaceInspector.App.ViewModels;

public sealed class ConsumerStorageToolViewModel
{
    public ConsumerStorageToolViewModel(
        string title,
        string value,
        string detail,
        string action,
        string searchQuery,
        int workspaceIndex)
    {
        Title = title;
        Value = value;
        Detail = detail;
        Action = action;
        SearchQuery = searchQuery;
        WorkspaceIndex = workspaceIndex;
    }

    public string Title { get; }

    public string Value { get; }

    public string Detail { get; }

    public string Action { get; }

    public string SearchQuery { get; }

    public int WorkspaceIndex { get; }
}
