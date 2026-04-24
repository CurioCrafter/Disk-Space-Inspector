using DiskSpaceInspector.App.Infrastructure;
using DiskSpaceInspector.Core.Models;

namespace DiskSpaceInspector.App.ViewModels;

public sealed class InsightFindingViewModel
{
    public InsightFindingViewModel(InsightFinding insight)
    {
        Model = insight;
    }

    public InsightFinding Model { get; }

    public string Tool => Model.Tool;

    public string Title => Model.Title;

    public string Path => Model.Path;

    public string Size => ByteFormatter.Format(Model.SizeBytes);

    public string Safety => DisplayText.ForSafety(Model.Safety);

    public string Action => DisplayText.ForAction(Model.RecommendedAction);

    public string Confidence => $"{Model.Confidence:P0}";

    public string Description => Model.Description;

    public string Evidence => Model.Evidence;
}
