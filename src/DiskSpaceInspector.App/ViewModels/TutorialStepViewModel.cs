using DiskSpaceInspector.Core.Models;

namespace DiskSpaceInspector.App.ViewModels;

public sealed class TutorialStepViewModel
{
    public TutorialStepViewModel(TutorialStep step, int number)
    {
        Step = step;
        Number = number;
    }

    public TutorialStep Step { get; }

    public int Number { get; }

    public string NumberText => Number.ToString("00");

    public string Title => Step.Title;

    public string Goal => Step.Goal;

    public string Body => Step.Body;

    public string Action => Step.Action;

    public string SafetyNote => Step.SafetyNote;
}
