namespace DiskSpaceInspector.Core.Ai;

public sealed class ProcessRunRequest
{
    public string FileName { get; init; } = string.Empty;

    public IReadOnlyList<string> Arguments { get; init; } = [];

    public string? StandardInput { get; init; }

    public string? WorkingDirectory { get; init; }

    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(2);
}
