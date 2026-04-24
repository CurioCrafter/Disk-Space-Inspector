using DiskSpaceInspector.App.Infrastructure;
using DiskSpaceInspector.Core.Models;

namespace DiskSpaceInspector.App.ViewModels;

public sealed class ChangeRecordViewModel
{
    public ChangeRecordViewModel(ChangeRecord record)
    {
        Model = record;
    }

    public ChangeRecord Model { get; }

    public string Kind => Model.Kind.ToString();

    public string Path => Model.Path;

    public string PreviousPath => Model.PreviousPath ?? "";

    public string Delta => ByteFormatter.Format(Math.Abs(Model.DeltaBytes));

    public string Direction => Model.DeltaBytes switch
    {
        > 0 => "Grew",
        < 0 => "Shrank",
        _ => "Changed"
    };

    public string CurrentSize => ByteFormatter.Format(Model.CurrentSizeBytes);

    public string PreviousSize => ByteFormatter.Format(Model.PreviousSizeBytes);

    public string Reason => Model.Reason;
}
