using DiskSpaceInspector.Core.Models;

namespace DiskSpaceInspector.App.ViewModels;

public sealed class ChartDefinitionViewModel
{
    public ChartDefinitionViewModel(ChartDefinition definition)
    {
        Definition = definition;
    }

    public ChartDefinition Definition { get; }

    public string Key => Definition.Key;

    public string Title => Definition.Title;

    public string Group => Definition.Group;

    public string Description => Definition.Description;

    public string Insight => Definition.Insight;

    public string Kind => Definition.Kind.ToString();

    public bool IsAdvanced => Definition.IsAdvanced;

    public bool HasInsight => !string.IsNullOrWhiteSpace(Definition.Insight);
}
