using DiskSpaceInspector.App.Infrastructure;
using DiskSpaceInspector.Core.Models;

namespace DiskSpaceInspector.App.ViewModels;

public sealed class VolumeViewModel
{
    public VolumeViewModel(VolumeInfo model)
    {
        Model = model;
    }

    public VolumeInfo Model { get; }

    public string Name => Model.DisplayName;

    public string Root => Model.RootPath;

    public string Type => Model.DriveType;

    public string FileSystem => Model.FileSystem ?? "";

    public string Used => Model.TotalBytes > 0 ? ByteFormatter.Format(Model.TotalBytes - Model.FreeBytes) : "Unknown";

    public string Free => Model.TotalBytes > 0 ? ByteFormatter.Format(Model.FreeBytes) : "Unknown";

    public string Total => Model.TotalBytes > 0 ? ByteFormatter.Format(Model.TotalBytes) : "Unknown";

    public double UsedPercent => Model.TotalBytes > 0 ? (Model.TotalBytes - Model.FreeBytes) / (double)Model.TotalBytes : 0;

    public string Status => Model.IsReady ? "Ready" : "Unavailable";
}
