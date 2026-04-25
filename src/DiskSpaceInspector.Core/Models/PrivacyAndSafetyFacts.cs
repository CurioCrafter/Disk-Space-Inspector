namespace DiskSpaceInspector.Core.Models;

public static class PrivacyAndSafetyFacts
{
    public const string TelemetryMode = "None";

    public const bool NetworkTelemetryEnabled = false;

    public const string ExternalIntegrationPolicy =
        "Disk Space Inspector does not use external advisor services, cloud telemetry, or credential integrations.";

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
