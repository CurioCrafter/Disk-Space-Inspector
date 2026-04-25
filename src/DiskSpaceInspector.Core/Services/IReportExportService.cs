using DiskSpaceInspector.Core.Models;

namespace DiskSpaceInspector.Core.Services;

public interface IReportExportService
{
    Task<ReportBundle> ExportAsync(
        ScanResult scan,
        IEnumerable<CleanupFinding> findings,
        IEnumerable<StorageRelationship> relationships,
        IEnumerable<ChangeRecord> changes,
        IEnumerable<InsightFinding> insights,
        ReportExportOptions options,
        CancellationToken cancellationToken = default);
}
