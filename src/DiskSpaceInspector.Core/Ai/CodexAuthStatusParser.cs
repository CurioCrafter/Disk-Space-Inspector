using DiskSpaceInspector.Core.Models;

namespace DiskSpaceInspector.Core.Ai;

public static class CodexAuthStatusParser
{
    public static CodexAuthStatus Parse(int exitCode, string standardOutput, string standardError)
    {
        var combined = $"{standardOutput}\n{standardError}".Trim();
        if (combined.Contains("using ChatGPT", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("with ChatGPT", StringComparison.OrdinalIgnoreCase))
        {
            return new CodexAuthStatus
            {
                Kind = CodexAuthKind.ChatGpt,
                DisplayText = "Logged in with ChatGPT.",
                Detail = combined
            };
        }

        if (combined.Contains("api key", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("using API", StringComparison.OrdinalIgnoreCase))
        {
            return new CodexAuthStatus
            {
                Kind = CodexAuthKind.ApiKey,
                DisplayText = "Wrong auth mode: Codex is logged in with an API key. Disk Space Inspector needs ChatGPT/Codex login.",
                Detail = combined
            };
        }

        if (combined.Contains("not logged", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("not authenticated", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("login required", StringComparison.OrdinalIgnoreCase))
        {
            return new CodexAuthStatus
            {
                Kind = CodexAuthKind.NotLoggedIn,
                DisplayText = "Not logged in to Codex.",
                Detail = combined
            };
        }

        if (exitCode != 0)
        {
            return new CodexAuthStatus
            {
                Kind = CodexAuthKind.NotLoggedIn,
                DisplayText = "Not logged in to Codex.",
                Detail = combined
            };
        }

        return new CodexAuthStatus
        {
            Kind = CodexAuthKind.Unknown,
            DisplayText = "Codex status unknown.",
            Detail = combined
        };
    }

    public static CodexAuthStatus NotInstalled(string detail)
    {
        return new CodexAuthStatus
        {
            Kind = CodexAuthKind.CodexNotInstalled,
            DisplayText = "Codex not installed. Install with: npm i -g @openai/codex",
            Detail = detail
        };
    }

    public static CodexAuthStatus Error(string detail)
    {
        return new CodexAuthStatus
        {
            Kind = CodexAuthKind.Error,
            DisplayText = "Could not check Codex login status.",
            Detail = detail
        };
    }
}
