using System;
using System.Collections.Generic;
using System.Linq;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Platform;
using BIM765T.Revit.Copilot.Core.Brain;

namespace BIM765T.Revit.WorkerHost.Routing;

internal sealed class BoundedMissionPlanner : IBoundedMissionPlanner
{
    private static readonly HashSet<string> ReadOnlyIntents = new(StringComparer.OrdinalIgnoreCase)
    {
        "greeting",
        "help",
        "context_query",
        "project_research_request",
        "sheet_analysis_request",
        "family_analysis_request",
        "qc_request"
    };

    private readonly ILlmPlanner _planner;

    public BoundedMissionPlanner()
        : this(new NullLlmPlanner())
    {
    }

    public BoundedMissionPlanner(ILlmPlanner planner)
    {
        _planner = planner ?? throw new ArgumentNullException(nameof(planner));
    }

    public MissionPlan BuildPlan(MissionPlanningContext context, MissionCandidateSet candidates)
    {
        context ??= new MissionPlanningContext();
        candidates ??= new MissionCandidateSet();

        var validation = _planner.Plan(new LlmPlanningRequest
        {
            Session = candidates.Session ?? new WorkerConversationSessionState(),
            RuleDecision = candidates.RuleDecision ?? new WorkerDecision(),
            Classification = candidates.Classification ?? new WorkerIntentClassification(),
            Persona = candidates.Persona ?? new WorkerPersonaSummary(),
            ContextSummary = candidates.ContextSummary ?? new WorkerContextSummary(),
            WorkspaceId = candidates.WorkspaceId ?? "default",
            UserMessage = context.UserMessage ?? string.Empty,
            ContinueMission = context.ContinueMission,
            AutonomyMode = FirstNonEmpty(candidates.AutonomyMode, context.AutonomyMode, WorkerAutonomyModes.Bounded),
            CandidateTools = candidates.CandidateTools?.ToList() ?? new List<string>(),
            CandidateCommands = candidates.CandidateCommands?.ToList() ?? new List<string>()
        });

        var chosenTools = (validation.Accepted && validation.PlannedTools.Count > 0
                ? validation.PlannedTools
                : candidates.CandidateTools?.Count > 0
                    ? candidates.CandidateTools
                    : candidates.RuleDecision?.PlannedTools ?? new List<string>())
            .Where(tool => !string.IsNullOrWhiteSpace(tool))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var intent = FirstNonEmpty(
            validation.Accepted ? validation.Proposal?.Intent : string.Empty,
            candidates.RuleDecision?.Intent,
            candidates.Classification?.Intent,
            "help");
        var planSummary = FirstNonEmpty(
            validation.Accepted ? validation.Proposal?.PlanSummary : string.Empty,
            candidates.RuleDecision?.PlanSummary,
            $"Intent={intent}");
        var reasoningSummary = FirstNonEmpty(
            validation.Accepted ? validation.Proposal?.ReasoningSummary : string.Empty,
            candidates.RuleDecision?.ReasoningSummary,
            $"Intent={intent}");
        var preferredCommandId = FirstNonEmpty(validation.PreferredCommandId, candidates.RuleDecision?.PreferredCommandId);
        var approvalRequired = ResolveApprovalRequired(intent, chosenTools, preferredCommandId);
        var autonomyMode = FirstNonEmpty(candidates.AutonomyMode, context.AutonomyMode, WorkerAutonomyModes.Bounded);

        return new MissionPlan
        {
            Intent = intent,
            TargetHint = candidates.Classification?.TargetHint ?? string.Empty,
            ToolName = ToolNames.WorkerMessage,
            Summary = planSummary,
            CommandText = FirstNonEmpty(preferredCommandId, intent),
            ContinueMission = context.ContinueMission,
            DryRun = !string.Equals(intent, "approval", StringComparison.OrdinalIgnoreCase),
            FlowState = ResolveFlowState(intent, chosenTools, approvalRequired),
            ChosenToolSequence = chosenTools,
            GroundingLevel = ResolveGroundingLevel(chosenTools),
            EvidenceRefs = candidates.ContextSummary?.GroundingRefs?.Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? new List<string>(),
            ApprovalRequired = approvalRequired,
            PlannerTraceSummary = BuildPlannerTrace(validation, candidates.RuleDecision, chosenTools, reasoningSummary),
            AutonomyMode = autonomyMode
        };
    }

    private static bool ResolveApprovalRequired(string intent, IReadOnlyCollection<string> chosenTools, string preferredCommandId)
    {
        if (string.Equals(intent, "approval", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (ReadOnlyIntents.Contains(intent))
        {
            return false;
        }

        if (chosenTools.Contains(ToolNames.CommandExecuteSafe, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(preferredCommandId)
            && (preferredCommandId.Contains("create", StringComparison.OrdinalIgnoreCase)
                || preferredCommandId.Contains("apply", StringComparison.OrdinalIgnoreCase)
                || preferredCommandId.Contains("rename", StringComparison.OrdinalIgnoreCase)
                || preferredCommandId.Contains("place", StringComparison.OrdinalIgnoreCase)
                || preferredCommandId.Contains("delete", StringComparison.OrdinalIgnoreCase));
    }

    private static string ResolveFlowState(string intent, IReadOnlyCollection<string> chosenTools, bool approvalRequired)
    {
        if (string.Equals(intent, "approval", StringComparison.OrdinalIgnoreCase))
        {
            return WorkerFlowStages.Approval;
        }

        if (approvalRequired)
        {
            return WorkerFlowStages.Preview;
        }

        if (chosenTools.Contains(ToolNames.ReviewSmartQc, StringComparer.OrdinalIgnoreCase)
            || chosenTools.Contains(ToolNames.ReviewModelHealth, StringComparer.OrdinalIgnoreCase))
        {
            return WorkerFlowStages.Verify;
        }

        if (chosenTools.Contains(ToolNames.ProjectGetDeepScan, StringComparer.OrdinalIgnoreCase)
            || chosenTools.Contains(ToolNames.ProjectGetContextBundle, StringComparer.OrdinalIgnoreCase)
            || chosenTools.Contains(ToolNames.SessionGetTaskContext, StringComparer.OrdinalIgnoreCase))
        {
            return WorkerFlowStages.Scan;
        }

        return WorkerFlowStages.Plan;
    }

    private static string ResolveGroundingLevel(IReadOnlyCollection<string> chosenTools)
    {
        if (chosenTools.Contains(ToolNames.ProjectGetDeepScan, StringComparer.OrdinalIgnoreCase)
            || chosenTools.Contains(ToolNames.ArtifactSummarize, StringComparer.OrdinalIgnoreCase)
            || chosenTools.Contains(ToolNames.MemorySearchScoped, StringComparer.OrdinalIgnoreCase)
            || chosenTools.Contains(ToolNames.StandardsResolve, StringComparer.OrdinalIgnoreCase))
        {
            return WorkerGroundingLevels.DeepScanGrounded;
        }

        if (chosenTools.Contains(ToolNames.ProjectGetContextBundle, StringComparer.OrdinalIgnoreCase))
        {
            return WorkerGroundingLevels.WorkspaceGrounded;
        }

        return WorkerGroundingLevels.LiveContextOnly;
    }

    private static string BuildPlannerTrace(LlmPlanValidationResult validation, WorkerDecision? decision, IReadOnlyCollection<string> chosenTools, string reasoningSummary)
    {
        if (validation.Accepted)
        {
            return $"LLM bounded planner accepted ({validation.ConfiguredProvider} {validation.PlannerModel}); tools={string.Join(", ", chosenTools)}; summary={FirstNonEmpty(validation.Proposal?.ReasoningSummary, reasoningSummary)}";
        }

        return $"Rule-first fast-path ({validation.Reason}); tools={string.Join(", ", chosenTools)}; summary={FirstNonEmpty(decision?.ReasoningSummary, reasoningSummary)}";
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    }
}
