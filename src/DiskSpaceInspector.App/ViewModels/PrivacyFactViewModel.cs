namespace DiskSpaceInspector.App.ViewModels;

public sealed class PrivacyFactViewModel
{
    public PrivacyFactViewModel(string label, string value, string detail)
    {
        Label = label;
        Value = value;
        Detail = detail;
    }

    public string Label { get; }

    public string Value { get; }

    public string Detail { get; }
}
