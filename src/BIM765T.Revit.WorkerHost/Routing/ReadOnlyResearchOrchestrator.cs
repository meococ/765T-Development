using System;
using System.Collections.Generic;
using System.Linq;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.WorkerHost.Routing;

internal sealed class ReadOnlyResearchOrchestrator : IReadOnlyResearchOrchestrator
{
    public MissionPlan Decorate(MissionPlan plan, RetrievalContext retrieval, MissionPlanningContext context)
    {
        plan ??= new MissionPlan();
        retrieval ??= new RetrievalContext();
        context ??= new MissionPlanningContext();

        var evidenceRefs = new List<string>(plan.EvidenceRefs ?? new List<string>());
        foreach (var sourceRef in retrieval.Hits
                     .Select(hit => hit.SourceRef)
                     .Where(sourceRef => !string.IsNullOrWhiteSpace(sourceRef))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            evidenceRefs.Add(sourceRef);
        }

        if (!string.IsNullOrWhiteSpace(context.DocumentKey))
        {
            evidenceRefs.Add("doc:" + context.DocumentKey.Trim());
        }

        if (!string.IsNullOrWhiteSpace(context.TargetView))
        {
            evidenceRefs.Add("view:" + context.TargetView.Trim());
        }

        if (!string.IsNullOrWhiteSpace(context.WorkspaceId))
        {
            evidenceRefs.Add("workspace:" + context.WorkspaceId.Trim());
        }

        plan.EvidenceRefs = evidenceRefs
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (string.IsNullOrWhiteSpace(plan.GroundingLevel))
        {
            plan.GroundingLevel = WorkerGroundingLevels.LiveContextOnly;
        }

        if (!string.IsNullOrWhiteSpace(context.WorkspaceId)
            && !string.Equals(context.WorkspaceId, "default", StringComparison.OrdinalIgnoreCase)
            && string.Equals(plan.GroundingLevel, WorkerGroundingLevels.LiveContextOnly, StringComparison.OrdinalIgnoreCase))
        {
            plan.GroundingLevel = WorkerGroundingLevels.WorkspaceGrounded;
        }

        if (plan.ChosenToolSequence.Contains(ToolNames.ProjectGetDeepScan, StringComparer.OrdinalIgnoreCase)
            || plan.ChosenToolSequence.Contains(ToolNames.ArtifactSummarize, StringComparer.OrdinalIgnoreCase)
            || plan.ChosenToolSequence.Contains(ToolNames.StandardsResolve, StringComparer.OrdinalIgnoreCase)
            || plan.ChosenToolSequence.Contains(ToolNames.MemorySearchScoped, StringComparer.OrdinalIgnoreCase))
        {
            plan.GroundingLevel = WorkerGroundingLevels.DeepScanGrounded;
        }

        var groundingSummary = BuildGroundingSummary(plan, retrieval);
        plan.Summary = string.IsNullOrWhiteSpace(plan.Summary)
            ? groundingSummary
            : $"{plan.Summary} {groundingSummary}".Trim();
        plan.PlannerTraceSummary = string.IsNullOrWhiteSpace(plan.PlannerTraceSummary)
            ? groundingSummary
            : $"{plan.PlannerTraceSummary} | {groundingSummary}";
        return plan;
    }

    private static string BuildGroundingSummary(MissionPlan plan, RetrievalContext retrieval)
    {
        var memorySummary = retrieval.Hits.Count == 0
            ? "memory:0"
            : $"memory:{retrieval.Hits.Count}";
        var levelSummary = plan.GroundingLevel switch
        {
            WorkerGroundingLevels.DeepScanGrounded => "grounding=deep_scan_grounded",
            WorkerGroundingLevels.WorkspaceGrounded => "grounding=workspace_grounded",
            _ => "grounding=live_context_only"
        };

        return $"[{levelSummary}] {memorySummary}; tools={string.Join(", ", plan.ChosenToolSequence)}";
    }
}
