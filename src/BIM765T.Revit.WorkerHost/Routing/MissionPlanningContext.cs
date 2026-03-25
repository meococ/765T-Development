using System.Collections.Generic;
using BIM765T.Revit.Contracts.Platform;
using BIM765T.Revit.Copilot.Core.Brain;

namespace BIM765T.Revit.WorkerHost.Routing;

internal sealed class MissionPlanningContext
{
    public string SessionId { get; set; } = string.Empty;

    public string PersonaId { get; set; } = WorkerPersonas.RevitWorker;

    public string ClientSurface { get; set; } = WorkerClientSurfaces.Mcp;

    public string UserMessage { get; set; } = string.Empty;

    public string DocumentKey { get; set; } = string.Empty;

    public string TargetDocument { get; set; } = string.Empty;

    public string TargetView { get; set; } = string.Empty;

    public string WorkspaceId { get; set; } = "default";

    public bool ContinueMission { get; set; } = true;

    public WorkerPendingApprovalState PendingApprovalState { get; set; } = new WorkerPendingApprovalState();

    public string AutonomyMode { get; set; } = WorkerAutonomyModes.Bounded;
}

internal sealed class MissionCandidateSet
{
    public WorkerConversationSessionState Session { get; set; } = new WorkerConversationSessionState();

    public WorkerPersonaSummary Persona { get; set; } = new WorkerPersonaSummary();

    public WorkerIntentClassification Classification { get; set; } = new WorkerIntentClassification();

    public WorkerDecision RuleDecision { get; set; } = new WorkerDecision();

    public WorkerContextSummary ContextSummary { get; set; } = new WorkerContextSummary();

    public string WorkspaceId { get; set; } = "default";

    public string AutonomyMode { get; set; } = WorkerAutonomyModes.Bounded;

    public List<string> CandidateTools { get; set; } = new List<string>();

    public List<string> CandidateCommands { get; set; } = new List<string>();
}
