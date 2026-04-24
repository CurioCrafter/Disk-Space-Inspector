using DiskSpaceInspector.App.Infrastructure;
using DiskSpaceInspector.Core.Models;

namespace DiskSpaceInspector.App.ViewModels;

public sealed class AiCleanupRecommendationViewModel
{
    public AiCleanupRecommendationViewModel(AiCleanupRecommendation recommendation)
    {
        Model = recommendation;
    }

    public AiCleanupRecommendation Model { get; }

    public string DisplayName => Model.DisplayName;

    public string Path => Model.Path;

    public string Size => ByteFormatter.Format(Model.SizeBytes);

    public string Safety => DisplayText.ForSafety(Model.Safety);

    public string Action => DisplayText.ForAction(Model.RecommendedAction);

    public string Recommendation => Model.AiVerb;

    public string Confidence => $"{Model.Confidence:P0}";

    public string Reasoning => Model.Reasoning;

    public string Evidence => Model.Evidence;

    public string Guardrail => Model.Guardrail;

    public string CanStage => Model.CanStage ? "Yes" : "No";
}
