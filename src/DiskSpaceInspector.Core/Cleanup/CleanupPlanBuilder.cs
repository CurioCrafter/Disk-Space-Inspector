using DiskSpaceInspector.Core.Models;

namespace DiskSpaceInspector.Core.Cleanup;

public sealed class CleanupPlanBuilder
{
    public CleanupActionPlan Build(IEnumerable<CleanupFinding> selectedFindings)
    {
        var findings = selectedFindings.ToList();
        var executable = findings
            .Where(f => f.Safety is CleanupSafety.Safe or CleanupSafety.Review)
            .Where(f => f.RecommendedAction != CleanupActionKind.LeaveAlone)
            .ToList();

        return new CleanupActionPlan
        {
            Findings = executable,
            BlockedCount = findings.Count(f => f.Safety == CleanupSafety.Blocked),
            SystemCleanupCount = findings.Count(f => f.Safety == CleanupSafety.UseSystemCleanup)
        };
    }
}
