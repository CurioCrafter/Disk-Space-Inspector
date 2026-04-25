using DiskSpaceInspector.App.Infrastructure;
using DiskSpaceInspector.Core.Models;

namespace DiskSpaceInspector.App.ViewModels;

public sealed class CleanupReviewItemViewModel
{
    public CleanupReviewItemViewModel(CleanupReviewItem item)
    {
        Model = item;
    }

    public CleanupReviewItem Model { get; }

    public string DisplayName => Model.DisplayName;

    public string Path => Model.Path;

    public string Safety => DisplayText.ForSafety(Model.Safety);

    public string Action => DisplayText.ForAction(Model.RecommendedAction);

    public string Size => ByteFormatter.Format(Model.SizeBytes);

    public string Files => Model.FileCount.ToString("n0");

    public string Evidence => Model.Evidence;
}
