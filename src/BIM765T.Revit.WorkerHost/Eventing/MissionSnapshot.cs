using System.Collections.Generic;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.WorkerHost.Eventing;

internal sealed class MissionSnapshot
{
    public string MissionId { get; set; } = string.Empty;

    public long Version { get; set; }

    public string State { get; set; } = "planned";

    public string SessionId { get; set; } = string.Empty;

    public string DocumentKey { get; set; } = string.Empty;

    public string TargetDocument { get; set; } = string.Empty;

    public string TargetView { get; set; } = string.Empty;

    public string Intent { get; set; } = string.Empty;

    public string RequestJson { get; set; } = "{}";

    public string ResponseJson { get; set; } = "{}";

    public string ResponseText { get; set; } = string.Empty;

    public string ApprovalToken { get; set; } = string.Empty;

    public string PreviewRunId { get; set; } = string.Empty;

    public string ExpectedContextJson { get; set; } = string.Empty;

    public string LastStatusCode { get; set; } = string.Empty;

    public bool Terminal { get; set; }

    public string FlowState { get; set; } = string.Empty;

    public string GroundingLevel { get; set; } = string.Empty;

    public string PlanSummary { get; set; } = string.Empty;

    public string PlannerTraceSummary { get; set; } = string.Empty;

    public bool ApprovalRequired { get; set; }

    public List<string> ChosenToolSequence { get; set; } = new List<string>();

    public List<string> EvidenceRefs { get; set; } = new List<string>();

    public string AutonomyMode { get; set; } = WorkerAutonomyModes.Bounded;
}
