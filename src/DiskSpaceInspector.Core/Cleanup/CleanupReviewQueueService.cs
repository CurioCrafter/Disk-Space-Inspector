using System.Text.Json;
using System.Text.RegularExpressions;
using DiskSpaceInspector.Core.Models;
using DiskSpaceInspector.Core.Services;

namespace DiskSpaceInspector.Core.Cleanup;

public sealed class CleanupReviewQueueService : ICleanupReviewQueueService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private static readonly Regex UserProfilePattern = new(
        @"(?i)\b[A-Z]:\\Users\\[^\\/:*?""<>|]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public CleanupReviewResult TryStage(CleanupReviewQueue queue, CleanupFinding finding)
    {
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(finding);

        if (finding.Safety == CleanupSafety.Blocked)
        {
            return Result(queue, "Blocked paths cannot be staged. Use the explanation only.");
        }

        if (finding.Safety == CleanupSafety.UseSystemCleanup)
        {
            return Result(queue, "Use the Windows or app cleanup route for this item; direct cleanup stays disabled.");
        }

        if (finding.RecommendedAction == CleanupActionKind.LeaveAlone)
        {
            return Result(queue, "This recommendation is informational and should be left alone.");
        }

        if (queue.Items.Any(item =>
                item.FindingId == finding.Id ||
                item.Path.Equals(finding.Path, StringComparison.OrdinalIgnoreCase)))
        {
            return new CleanupReviewResult
            {
                Queue = queue,
                AlreadyStaged = true,
                Message = "That item is already in the cleanup review queue."
            };
        }

        queue.Items.Add(new CleanupReviewItem
        {
            FindingId = finding.Id,
            NodeId = finding.NodeId,
            Path = finding.Path,
            DisplayName = finding.DisplayName,
            Category = finding.Category,
            Safety = finding.Safety,
            RecommendedAction = finding.RecommendedAction,
            SizeBytes = finding.SizeBytes,
            FileCount = finding.FileCount,
            Confidence = finding.Confidence,
            Evidence = BuildEvidence(finding),
            Explanation = finding.Explanation
        });

        return new CleanupReviewResult
        {
            Added = true,
            Queue = queue,
            Message = $"Added {finding.DisplayName} to the cleanup review queue."
        };
    }

    public CleanupReviewQueue Remove(CleanupReviewQueue queue, Guid findingId)
    {
        ArgumentNullException.ThrowIfNull(queue);
        queue.Items.RemoveAll(item => item.FindingId == findingId);
        return queue;
    }

    public CleanupReviewQueue Clear(CleanupReviewQueue queue)
    {
        ArgumentNullException.ThrowIfNull(queue);
        queue.Items.Clear();
        return queue;
    }

    public async Task<CleanupReviewExport> ExportAsync(
        CleanupReviewQueue queue,
        string outputDirectory,
        PathPrivacyMode privacyMode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(queue);

        Directory.CreateDirectory(outputDirectory);
        var redactedCount = 0;
        string Redact(string value)
        {
            if (privacyMode == PathPrivacyMode.Raw || string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            var replaced = UserProfilePattern.Replace(value, _ =>
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

        var exportedItems = queue.Items
            .OrderBy(item => item.Safety)
            .ThenByDescending(item => item.SizeBytes)
            .Select(item => new
            {
                path = Redact(item.Path),
                item.DisplayName,
                item.Category,
                safety = item.Safety.ToString(),
                action = item.RecommendedAction.ToString(),
                item.SizeBytes,
                item.FileCount,
                item.Confidence,
                item.Evidence,
                item.Explanation,
                item.AddedAtUtc
            })
            .ToList();

        var dataPath = Path.Combine(outputDirectory, "cleanup-review-queue.json");
        await File.WriteAllTextAsync(
            dataPath,
            JsonSerializer.Serialize(new
            {
                product = "Disk Space Inspector",
                createdAtUtc = DateTimeOffset.UtcNow,
                itemCount = exportedItems.Count,
                totalBytes = queue.TotalBytes,
                privacyMode = privacyMode.ToString(),
                items = exportedItems
            }, JsonOptions),
            cancellationToken).ConfigureAwait(false);

        var summaryPath = Path.Combine(outputDirectory, "cleanup-review-summary.md");
        await File.WriteAllTextAsync(
            summaryPath,
            BuildSummary(queue, exportedItems.Count, redactedCount),
            cancellationToken).ConfigureAwait(false);

        return new CleanupReviewExport
        {
            DirectoryPath = outputDirectory,
            SummaryPath = summaryPath,
            DataPath = dataPath,
            RedactedPathCount = redactedCount
        };
    }

    private static CleanupReviewResult Result(CleanupReviewQueue queue, string message)
    {
        return new CleanupReviewResult { Queue = queue, Message = message };
    }

    private static string BuildEvidence(CleanupFinding finding)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(finding.MatchedRule))
        {
            parts.Add($"Rule: {finding.MatchedRule}");
        }

        if (!string.IsNullOrWhiteSpace(finding.AppOrSource))
        {
            parts.Add($"Source: {finding.AppOrSource}");
        }

        foreach (var pair in finding.Evidence.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase).Take(4))
        {
            parts.Add($"{pair.Key}: {pair.Value}");
        }

        return string.Join("; ", parts);
    }

    private static string BuildSummary(CleanupReviewQueue queue, int exportedCount, int redactedCount)
    {
        return $"""
            # Disk Space Inspector Cleanup Review Queue

            Generated: {DateTimeOffset.UtcNow:O}

            This export is a review checklist, not an instruction to delete files automatically.

            ## Summary
            - Items: {exportedCount:n0}
            - Estimated reclaimable bytes: {queue.TotalBytes:n0}
            - Files represented: {queue.TotalFileCount:n0}
            - Redacted paths: {redactedCount:n0}

            ## Safety
            - Safe items still require final review.
            - Review items need user or app context.
            - System and blocked paths are not allowed in this queue.
            """;
    }
}
