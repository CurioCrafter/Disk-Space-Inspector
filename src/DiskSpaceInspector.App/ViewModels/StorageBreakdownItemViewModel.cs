using DiskSpaceInspector.App.Infrastructure;
using DiskSpaceInspector.Core.Models;

namespace DiskSpaceInspector.App.ViewModels;

public sealed class StorageBreakdownItemViewModel
{
    public StorageBreakdownItemViewModel(StorageBreakdownItem item)
    {
        Model = item;
    }

    public StorageBreakdownItem Model { get; }

    public string Label => Enum.TryParse<CleanupSafety>(Model.Label, out var safety)
        ? DisplayText.ForSafety(safety)
        : Model.Label;

    public string Size => ByteFormatter.Format(Model.SizeBytes);

    public string Count => $"{Model.Count:n0}";

    public string Percent => $"{Model.Fraction:P0}";

    public double Fraction => Model.Fraction;

    public string ColorKey => Model.ColorKey;
}
