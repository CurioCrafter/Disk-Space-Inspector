namespace DiskSpaceInspector.Core.Models;

public sealed class ScanRequest
{
    public ScanRequest(VolumeInfo volume)
    {
        Volume = volume;
    }

    public VolumeInfo Volume { get; }

    public int MaxConcurrency { get; init; } = Math.Clamp(Environment.ProcessorCount, 2, 8);

    public bool IncludeHiddenAndSystemEntries { get; init; } = true;

    public ManualResetEventSlim? PauseGate { get; init; }

    public ScanPipelineOptions PipelineOptions { get; init; } = ScanPipelineOptions.DeepEnrichment;
}
