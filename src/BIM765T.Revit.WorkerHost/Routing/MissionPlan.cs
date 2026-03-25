using System.Collections.Generic;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.WorkerHost.Routing;

internal sealed class MissionPlan
{
    public string Intent { get; set; } = string.Empty;

    public string TargetHint { get; set; } = string.Empty;

    public string ToolName { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public string CommandText { get; set; } = string.Empty;

    public bool ContinueMission { get; set; }

    public bool DryRun { get; set; } = true;

    public string FlowState { get; set; } = string.Empty;

    public string GroundingLevel { get; set; } = string.Empty;

    public string PlannerTraceSummary { get; set; } = string.Empty;

    public bool ApprovalRequired { get; set; }

    public List<string> ChosenToolSequence { get; set; } = new List<string>();

    public List<string> EvidenceRefs { get; set; } = new List<string>();

    public string AutonomyMode { get; set; } = WorkerAutonomyModes.Bounded;
}
