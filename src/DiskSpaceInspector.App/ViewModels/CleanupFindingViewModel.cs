using DiskSpaceInspector.App.Infrastructure;
using DiskSpaceInspector.Core.Models;

namespace DiskSpaceInspector.App.ViewModels;

public sealed class CleanupFindingViewModel
{
    public CleanupFindingViewModel(CleanupFinding finding)
    {
        Model = finding;
    }

    public CleanupFinding Model { get; }

    public string DisplayName => Model.DisplayName;

    public string Path => Model.Path;

    public string Category => Model.Category;

    public string Safety => DisplayText.ForSafety(Model.Safety);

    public string Action => DisplayText.ForAction(Model.RecommendedAction);

    public string Size => ByteFormatter.Format(Model.SizeBytes);

    public string Confidence => $"{Model.Confidence:P0}";

    public string Explanation => Model.Explanation;

}
