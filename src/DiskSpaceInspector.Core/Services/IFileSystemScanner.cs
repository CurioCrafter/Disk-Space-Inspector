using DiskSpaceInspector.Core.Models;

namespace DiskSpaceInspector.Core.Services;

public interface IFileSystemScanner
{
    Task<ScanCompleted> StartScanAsync(
        ScanRequest request,
        Func<ScanBatch, CancellationToken, Task> onBatchAsync,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<ScanResult> ScanAsync(
        ScanRequest request,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
