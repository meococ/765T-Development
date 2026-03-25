using System;
using System.Collections.Generic;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.WorkerHost.Routing;

internal sealed class PlannerAgent
{
    private readonly IToolCandidateBuilder _candidateBuilder;
    private readonly IBoundedMissionPlanner _boundedPlanner;
    private readonly IReadOnlyResearchOrchestrator _researchOrchestrator;

    public PlannerAgent()
        : this(new MissionToolCandidateBuilder(), new BoundedMissionPlanner(), new ReadOnlyResearchOrchestrator())
    {
    }

    public PlannerAgent(
        IToolCandidateBuilder candidateBuilder,
        IBoundedMissionPlanner boundedPlanner,
        IReadOnlyResearchOrchestrator researchOrchestrator)
    {
        _candidateBuilder = candidateBuilder ?? throw new ArgumentNullException(nameof(candidateBuilder));
        _boundedPlanner = boundedPlanner ?? throw new ArgumentNullException(nameof(boundedPlanner));
        _researchOrchestrator = researchOrchestrator ?? throw new ArgumentNullException(nameof(researchOrchestrator));
    }

    public MissionPlan BuildSubmissionPlan(MissionPlanningContext context, RetrievalContext? retrieval = null)
    {
        context ??= new MissionPlanningContext();
        var candidates = _candidateBuilder.Build(context);
        var plan = _boundedPlanner.BuildPlan(context, candidates);
        return _researchOrchestrator.Decorate(plan, retrieval ?? new RetrievalContext(), context);
    }

    public MissionPlan BuildCommandPlan(string commandName)
    {
        var normalized = NormalizeCommandName(commandName);
        return new MissionPlan
        {
            Intent = normalized,
            ToolName = BIM765T.Revit.Contracts.Bridge.ToolNames.WorkerMessage,
            Summary = $"Command={normalized}",
            CommandText = normalized,
            ContinueMission = true,
            DryRun = !string.Equals(normalized, "approval", StringComparison.OrdinalIgnoreCase),
            FlowState = ResolveCommandFlowState(normalized),
            GroundingLevel = WorkerGroundingLevels.LiveContextOnly,
            PlannerTraceSummary = $"Command fast-path normalized to '{normalized}'.",
            ApprovalRequired = string.Equals(normalized, "approval", StringComparison.OrdinalIgnoreCase),
            ChosenToolSequence = ResolveCommandSequence(normalized)
        };
    }

    public static string NormalizeCommandName(string commandName)
    {
        if (string.Equals(commandName, "approve", StringComparison.OrdinalIgnoreCase)
            || string.Equals(commandName, "approval", StringComparison.OrdinalIgnoreCase))
        {
            return "approval";
        }

        if (string.Equals(commandName, "reject", StringComparison.OrdinalIgnoreCase))
        {
            return "reject";
        }

        if (string.Equals(commandName, "cancel", StringComparison.OrdinalIgnoreCase))
        {
            return "cancel";
        }

        if (string.Equals(commandName, "resume", StringComparison.OrdinalIgnoreCase))
        {
            return "resume";
        }

        return string.IsNullOrWhiteSpace(commandName) ? "resume" : commandName.Trim().ToLowerInvariant();
    }

    private static string ResolveCommandFlowState(string normalized)
    {
        return normalized switch
        {
            "approval" => WorkerFlowStages.Approval,
            "resume" => WorkerFlowStages.Run,
            "cancel" => WorkerFlowStages.Error,
            "reject" => WorkerFlowStages.Error,
            _ => WorkerFlowStages.Plan
        };
    }

    private static List<string> ResolveCommandSequence(string normalized)
    {
        return normalized switch
        {
            "approval" => new List<string> { "pending_approval" },
            "resume" => new List<string> { "resume_mission" },
            "reject" => new List<string> { "reject_mission" },
            "cancel" => new List<string> { "cancel_mission" },
            _ => new List<string>()
        };
    }
}
