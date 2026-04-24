namespace DiskSpaceInspector.Core.Ai;

public sealed class ProcessRunResult
{
    public int ExitCode { get; init; }

    public string StandardOutput { get; init; } = string.Empty;

    public string StandardError { get; init; } = string.Empty;
}
