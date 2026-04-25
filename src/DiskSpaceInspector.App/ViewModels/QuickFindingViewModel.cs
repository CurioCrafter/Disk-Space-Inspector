namespace DiskSpaceInspector.App.ViewModels;

public sealed class QuickFindingViewModel
{
    public QuickFindingViewModel(string label, string value, string detail, string lane)
    {
        Label = label;
        Value = value;
        Detail = detail;
        Lane = lane;
    }

    public string Label { get; }

    public string Value { get; }

    public string Detail { get; }

    public string Lane { get; }
}
