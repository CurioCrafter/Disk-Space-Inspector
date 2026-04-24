using DiskSpaceInspector.App.Infrastructure;
using DiskSpaceInspector.Core.Models;

namespace DiskSpaceInspector.App.ViewModels;

public sealed class AgeHistogramBucketViewModel
{
    public AgeHistogramBucketViewModel(AgeHistogramBucket bucket)
    {
        Model = bucket;
    }

    public AgeHistogramBucket Model { get; }

    public string Label => Model.Label;

    public string Size => ByteFormatter.Format(Model.SizeBytes);

    public string Count => $"{Model.Count:n0}";

    public string Percent => $"{Model.Fraction:P0}";

    public double Fraction => Model.Fraction;
}
