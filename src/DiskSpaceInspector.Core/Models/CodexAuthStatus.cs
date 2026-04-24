namespace DiskSpaceInspector.Core.Models;

public sealed class CodexAuthStatus
{
    public CodexAuthKind Kind { get; init; } = CodexAuthKind.Unknown;

    public string DisplayText { get; init; } = "Codex status unknown.";

    public string Detail { get; init; } = string.Empty;

    public bool CanUseChatGpt => Kind == CodexAuthKind.ChatGpt;
}
