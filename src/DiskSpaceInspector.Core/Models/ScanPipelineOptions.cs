namespace DiskSpaceInspector.Core.Models;

public sealed class ScanPipelineOptions
{
    public ScanPerformanceMode PerformanceMode { get; init; } = ScanPerformanceMode.DeepEnrichment;

    public bool ResolveRelationshipsDuringScan { get; init; } = true;

    public bool ResolveStorageOwnershipDuringScan { get; init; } = true;

    public bool UseFastEnumeration { get; init; } = true;

    public static ScanPipelineOptions FastFirstScan { get; } = new()
    {
        PerformanceMode = ScanPerformanceMode.FastFirstScan,
        ResolveRelationshipsDuringScan = false,
        ResolveStorageOwnershipDuringScan = false,
        UseFastEnumeration = true
    };

    public static ScanPipelineOptions DeepEnrichment { get; } = new()
    {
        PerformanceMode = ScanPerformanceMode.DeepEnrichment,
        ResolveRelationshipsDuringScan = true,
        ResolveStorageOwnershipDuringScan = true,
        UseFastEnumeration = true
    };
}
