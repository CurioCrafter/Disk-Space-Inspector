namespace DiskSpaceInspector.Core.Models;

public static class PrivacyAndSafetyFacts
{
    public const string TelemetryMode = "None";

    public const bool NetworkTelemetryEnabled = false;

    public const string CodexCredentialPolicy =
        "Disk Space Inspector delegates sign-in to the Codex CLI and never reads or stores Codex OAuth tokens.";

    public static readonly IReadOnlyList<string> BlockedDirectCleanupPaths =
    [
        @"C:\Windows\WinSxS",
        @"C:\Windows\Installer",
        @"C:\Windows\System32",
        @"C:\Windows\System32\DriverStore",
        @"C:\pagefile.sys",
        @"C:\swapfile.sys",
        "Active browser profile databases",
        "Unknown app credential stores"
    ];
}
