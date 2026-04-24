using DiskSpaceInspector.Core.Models;

namespace DiskSpaceInspector.Core.Ai;

public static class AiCleanupRecommendationSafetyGate
{
    private static readonly Dictionary<string, CleanupSafety> SafetyAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["safe"] = CleanupSafety.Safe,
        ["review"] = CleanupSafety.Review,
        ["use system cleanup"] = CleanupSafety.UseSystemCleanup,
        ["usesystemcleanup"] = CleanupSafety.UseSystemCleanup,
        ["system"] = CleanupSafety.UseSystemCleanup,
        ["blocked"] = CleanupSafety.Blocked
    };

    public static IReadOnlyList<AiCleanupRecommendation> Apply(
        IReadOnlyList<CleanupFinding> candidates,
        IEnumerable<GeneratedAiCleanupRecommendation> generated,
        string model)
    {
        var byPath = candidates
            .GroupBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(f => f.Confidence).First(), StringComparer.OrdinalIgnoreCase);

        var recommendations = new List<AiCleanupRecommendation>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in generated)
        {
            if (string.IsNullOrWhiteSpace(item.Path) ||
                !byPath.TryGetValue(item.Path, out var finding) ||
                !seen.Add(finding.Path))
            {
                continue;
            }

            var modelSafety = ParseSafety(item.Risk);
            var finalSafety = MoreConservative(finding.Safety, modelSafety);
            var finalAction = SelectAction(finding, item.Recommendation, finalSafety);
            var canStage = finalSafety is CleanupSafety.Safe or CleanupSafety.Review &&
                           finalAction != CleanupActionKind.LeaveAlone &&
                           finalAction != CleanupActionKind.RunSystemCleanup;

            recommendations.Add(new AiCleanupRecommendation
            {
                SourceFindingId = finding.Id,
                NodeId = finding.NodeId,
                Path = finding.Path,
                DisplayName = finding.DisplayName,
                Category = finding.Category,
                Safety = finalSafety,
                RecommendedAction = finalAction,
                AiVerb = string.IsNullOrWhiteSpace(item.Recommendation) ? finalAction.ToString() : item.Recommendation.Trim(),
                SizeBytes = finding.SizeBytes,
                Confidence = Clamp(item.Confidence <= 0 ? finding.Confidence : Math.Min(item.Confidence, finding.Confidence + 0.1)),
                Reasoning = TrimOrFallback(item.Reasoning, finding.Explanation),
                Evidence = TrimOrFallback(item.Evidence, finding.MatchedRule),
                Guardrail = BuildGuardrail(finding.Safety, finalSafety, finalAction),
                CanStage = canStage,
                Model = model
            });
        }

        return recommendations
            .OrderBy(r => r.Safety)
            .ThenByDescending(r => r.SizeBytes)
            .ThenByDescending(r => r.Confidence)
            .ToList();
    }

    private static CleanupSafety ParseSafety(string value)
    {
        return SafetyAliases.TryGetValue(value.Trim(), out var safety) ? safety : CleanupSafety.Review;
    }

    private static CleanupSafety MoreConservative(CleanupSafety original, CleanupSafety model)
    {
        return Rank(model) > Rank(original) ? model : original;
    }

    private static int Rank(CleanupSafety safety)
    {
        return safety switch
        {
            CleanupSafety.Safe => 0,
            CleanupSafety.Review => 1,
            CleanupSafety.UseSystemCleanup => 2,
            CleanupSafety.Blocked => 3,
            _ => 3
        };
    }

    private static CleanupActionKind SelectAction(CleanupFinding finding, string recommendation, CleanupSafety finalSafety)
    {
        if (finalSafety == CleanupSafety.Blocked)
        {
            return CleanupActionKind.LeaveAlone;
        }

        if (finalSafety == CleanupSafety.UseSystemCleanup)
        {
            return CleanupActionKind.RunSystemCleanup;
        }

        if (Contains(recommendation, "leave"))
        {
            return CleanupActionKind.LeaveAlone;
        }

        if (finding.RecommendedAction == CleanupActionKind.EmptyRecycleBin &&
            Contains(recommendation, "recycle"))
        {
            return CleanupActionKind.EmptyRecycleBin;
        }

        if (finding.RecommendedAction == CleanupActionKind.ReviewDownloads &&
            Contains(recommendation, "download"))
        {
            return CleanupActionKind.ReviewDownloads;
        }

        if (finding.RecommendedAction == CleanupActionKind.ArchiveOrMove &&
            (Contains(recommendation, "archive") || Contains(recommendation, "move")))
        {
            return CleanupActionKind.ArchiveOrMove;
        }

        if (finding.RecommendedAction == CleanupActionKind.ClearCache &&
            (Contains(recommendation, "cache") || Contains(recommendation, "temp") || Contains(recommendation, "clear")))
        {
            return CleanupActionKind.ClearCache;
        }

        return finding.RecommendedAction;
    }

    private static bool Contains(string value, string fragment)
    {
        return value.Contains(fragment, StringComparison.OrdinalIgnoreCase);
    }

    private static string TrimOrFallback(string value, string fallback)
    {
        value = value.Trim();
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private static double Clamp(double value)
    {
        return Math.Max(0, Math.Min(1, value));
    }

    private static string BuildGuardrail(CleanupSafety originalSafety, CleanupSafety finalSafety, CleanupActionKind action)
    {
        if (originalSafety == CleanupSafety.Blocked)
        {
            return "Blocked by Disk Space Inspector safety rules; AI cannot make this executable.";
        }

        if (originalSafety == CleanupSafety.UseSystemCleanup || finalSafety == CleanupSafety.UseSystemCleanup)
        {
            return "Use the supported Windows or app cleanup route; no direct file deletion is staged.";
        }

        return action == CleanupActionKind.LeaveAlone
            ? "AI advised leaving this alone; no cleanup is staged."
            : "AI recommendation is constrained to the existing Disk Space Inspector cleanup finding.";
    }
}

public sealed class GeneratedAiCleanupRecommendation
{
    public string Path { get; init; } = string.Empty;

    public string Recommendation { get; init; } = string.Empty;

    public string Risk { get; init; } = "Review";

    public double Confidence { get; init; }

    public string Reasoning { get; init; } = string.Empty;

    public string Evidence { get; init; } = string.Empty;
}
