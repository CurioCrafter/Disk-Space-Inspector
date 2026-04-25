using System.Text.Json;
using System.Text.RegularExpressions;
using DiskSpaceInspector.Core.Models;
using DiskSpaceInspector.Core.Services;

namespace DiskSpaceInspector.Core.Reporting;

public sealed class ReportExportService : IReportExportService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private static readonly Regex UserProfilePattern = new(
        @"(?i)\b[A-Z]:\\Users\\[^\\/:*?""<>|]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public async Task<ReportBundle> ExportAsync(
        ScanResult scan,
        IEnumerable<CleanupFinding> findings,
        IEnumerable<StorageRelationship> relationships,
        IEnumerable<ChangeRecord> changes,
        IEnumerable<InsightFinding> insights,
        ReportExportOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scan);
        ArgumentNullException.ThrowIfNull(options);

        var createdAt = DateTimeOffset.UtcNow;
        var outputDirectory = string.IsNullOrWhiteSpace(options.OutputDirectory)
            ? Path.Combine(Path.GetTempPath(), "DiskSpaceInspectorReport")
            : options.OutputDirectory;
        Directory.CreateDirectory(outputDirectory);

        var redactedCount = 0;
        string Redact(string value)
        {
            if (options.PathPrivacyMode == PathPrivacyMode.Raw || string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            var replaced = UserProfilePattern.Replace(value, match =>
            {
                redactedCount++;
                return "%USERPROFILE%";
            });

            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(profile) && replaced.StartsWith(profile, StringComparison.OrdinalIgnoreCase))
            {
                redactedCount++;
                replaced = "%USERPROFILE%" + replaced[profile.Length..];
            }

            return replaced;
        }

        var findingList = findings
            .OrderBy(f => f.Safety)
            .ThenByDescending(f => f.SizeBytes)
            .Take(Math.Max(1, options.MaxFindings))
            .Select(f => new
            {
                path = Redact(f.Path),
                f.DisplayName,
                f.Category,
                safety = f.Safety.ToString(),
                action = f.RecommendedAction.ToString(),
                f.SizeBytes,
                f.FileCount,
                f.Confidence,
                f.Explanation,
                f.MatchedRule,
                f.AppOrSource
            })
            .ToList();

        var nodeList = scan.Nodes
            .OrderByDescending(n => Math.Max(n.TotalPhysicalLength, n.PhysicalLength))
            .ThenBy(n => n.FullPath, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, options.MaxNodes))
            .Select(n => new
            {
                path = Redact(n.FullPath),
                n.Name,
                kind = n.Kind.ToString(),
                n.Category,
                sizeBytes = Math.Max(n.TotalPhysicalLength, n.PhysicalLength),
                n.FileCount,
                n.FolderCount,
                n.Depth,
                n.LastModifiedUtc
            })
            .ToList();

        var relationshipList = options.IncludeRelationships
            ? relationships
                .OrderByDescending(r => r.Evidence.Confidence)
                .Take(250)
                .Select(r => new
                {
                    sourcePath = Redact(r.SourcePath),
                    targetPath = Redact(r.TargetPath ?? string.Empty),
                    kind = r.Kind.ToString(),
                    r.Label,
                    r.Owner,
                    evidenceSource = r.Evidence.Source,
                    evidenceDetail = r.Evidence.Detail,
                    r.Evidence.Confidence
                })
                .ToList()
            : [];

        var insightList = options.IncludeInsights
            ? insights
                .OrderByDescending(i => i.SizeBytes)
                .ThenByDescending(i => i.Confidence)
                .Take(250)
                .Select(i => new
                {
                    path = Redact(i.Path),
                    i.Tool,
                    i.Title,
                    i.Description,
                    safety = i.Safety.ToString(),
                    action = i.RecommendedAction.ToString(),
                    i.SizeBytes,
                    i.Confidence,
                    i.Evidence
                })
                .ToList()
            : [];

        var changeList = changes
            .OrderByDescending(c => Math.Abs(c.DeltaBytes))
            .Take(250)
            .Select(c => new
            {
                path = Redact(c.Path),
                previousPath = Redact(c.PreviousPath ?? string.Empty),
                kind = c.Kind.ToString(),
                c.PreviousSizeBytes,
                c.CurrentSizeBytes,
                c.DeltaBytes,
                c.Reason
            })
            .ToList();

        var data = new
        {
            product = "Disk Space Inspector",
            createdAtUtc = createdAt,
            privacy = new
            {
                telemetry = PrivacyAndSafetyFacts.TelemetryMode,
                networkTelemetryEnabled = PrivacyAndSafetyFacts.NetworkTelemetryEnabled,
                externalIntegrationPolicy = PrivacyAndSafetyFacts.ExternalIntegrationPolicy,
                pathPrivacyMode = options.PathPrivacyMode.ToString()
            },
            scan = new
            {
                id = scan.Session.Id,
                rootPath = Redact(scan.Session.RootPath),
                status = scan.Session.Status.ToString(),
                scan.Session.StartedAtUtc,
                scan.Session.CompletedAtUtc,
                scan.Session.FilesScanned,
                scan.Session.DirectoriesScanned,
                scan.Session.TotalPhysicalBytes,
                scan.Session.IssueCount
            },
            topNodes = nodeList,
            cleanupFindings = findingList,
            changes = changeList,
            relationships = relationshipList,
            insights = insightList,
            blockedDirectCleanupPaths = PrivacyAndSafetyFacts.BlockedDirectCleanupPaths
        };

        var dataPath = Path.Combine(outputDirectory, "diagnostics-data.json");
        await File.WriteAllTextAsync(
            dataPath,
            JsonSerializer.Serialize(data, JsonOptions),
            cancellationToken).ConfigureAwait(false);

        var summaryPath = Path.Combine(outputDirectory, "diagnostics-summary.md");
        await File.WriteAllTextAsync(
            summaryPath,
            BuildSummary(
                Redact(scan.Session.RootPath),
                scan,
                findingList.Count,
                nodeList.Count,
                changeList.Count,
                relationshipList.Count,
                insightList.Count,
                redactedCount),
            cancellationToken).ConfigureAwait(false);

        return new ReportBundle
        {
            DirectoryPath = outputDirectory,
            SummaryPath = summaryPath,
            DataPath = dataPath,
            CreatedAtUtc = createdAt,
            RedactedPathCount = redactedCount,
            FileCount = 2
        };
    }

    private static string BuildSummary(
        string redactedRootPath,
        ScanResult scan,
        int findings,
        int nodes,
        int changes,
        int relationships,
        int insights,
        int redactedPaths)
    {
        return $"""
            # Disk Space Inspector Diagnostics

            Generated: {DateTimeOffset.UtcNow:O}

            ## Scan
            - Root: {redactedRootPath}
            - Status: {scan.Session.Status}
            - Files: {scan.Session.FilesScanned:n0}
            - Folders: {scan.Session.DirectoriesScanned:n0}
            - Physical bytes: {scan.Session.TotalPhysicalBytes:n0}
            - Scan gaps: {scan.Session.IssueCount:n0}

            ## Included
            - Top nodes: {nodes:n0}
            - Cleanup findings: {findings:n0}
            - Changes: {changes:n0}
            - Relationships: {relationships:n0}
            - Insights: {insights:n0}

            ## Privacy
            - Telemetry: {PrivacyAndSafetyFacts.TelemetryMode}
            - Network telemetry enabled: {PrivacyAndSafetyFacts.NetworkTelemetryEnabled}
            - Redacted paths: {redactedPaths:n0}
            - External integrations: none; reports are generated locally.
            """;
    }
}
