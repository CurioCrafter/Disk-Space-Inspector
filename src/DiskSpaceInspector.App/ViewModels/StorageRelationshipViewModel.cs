using DiskSpaceInspector.Core.Models;

namespace DiskSpaceInspector.App.ViewModels;

public sealed class StorageRelationshipViewModel
{
    public StorageRelationshipViewModel(StorageRelationship relationship)
    {
        Kind = relationship.Kind.ToString();
        Label = relationship.Label;
        Owner = relationship.Owner;
        Source = relationship.SourcePath;
        Target = relationship.TargetPath ?? "";
        Evidence = $"{relationship.Evidence.Source}: {relationship.Evidence.Detail}";
        Confidence = $"{relationship.Evidence.Confidence:P0}";
    }

    public string Kind { get; }

    public string Label { get; }

    public string Owner { get; }

    public string Source { get; }

    public string Target { get; }

    public string Evidence { get; }

    public string Confidence { get; }
}
