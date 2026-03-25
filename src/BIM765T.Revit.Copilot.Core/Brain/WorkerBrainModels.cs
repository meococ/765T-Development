using System;
using System.Collections.Generic;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Copilot.Core.Brain;

public sealed class WorkerPendingApprovalState
{
    public string PendingActionId { get; set; } = string.Empty;

    public string ToolName { get; set; } = string.Empty;

    public string PayloadJson { get; set; } = string.Empty;

    public string ApprovalToken { get; set; } = string.Empty;

    public string PreviewRunId { get; set; } = string.Empty;

    public string ExpectedContextJson { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public DateTime? ExpiresUtc { get; set; }

    public string ExecutionTier { get; set; } = WorkerExecutionTiers.Tier0;

    public bool AutoExecutionEligible { get; set; }

    public bool HasPendingApproval => !string.IsNullOrWhiteSpace(PendingActionId);
}

public sealed class WorkerConversationSessionState
{
    public string SessionId { get; set; } = Guid.NewGuid().ToString("N");

    public string PersonaId { get; set; } = WorkerPersonas.RevitWorker;

    public string ClientSurface { get; set; } = WorkerClientSurfaces.Ui;

    public string Status { get; set; } = WorkerSessionStates.Active;

    public string DocumentKey { get; set; } = string.Empty;

    public DateTime StartedUtc { get; set; } = DateTime.UtcNow;

    public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;

    public List<WorkerChatMessage> Messages { get; set; } = new List<WorkerChatMessage>();

    public WorkerMission Mission { get; set; } = new WorkerMission();

    public WorkerContextSummary ContextSummary { get; set; } = new WorkerContextSummary();

    public WorkerPendingApprovalState PendingApprovalState { get; set; } = new WorkerPendingApprovalState();

    public WorkerSessionSummary ToSummary()
    {
        return new WorkerSessionSummary
        {
            SessionId = SessionId,
            PersonaId = PersonaId,
            Status = Status,
            DocumentKey = DocumentKey,
            MissionId = Mission.MissionId,
            LastUserMessage = Messages.Count == 0
                ? string.Empty
                : Messages.FindLast(x => string.Equals(x.Role, WorkerMessageRoles.User, StringComparison.OrdinalIgnoreCase))?.Content ?? string.Empty,
            StartedUtc = StartedUtc,
            LastUpdatedUtc = LastUpdatedUtc
        };
    }
}

public sealed class WorkerIntentClassification
{
    public string Intent { get; set; } = "help";

    public string NormalizedMessage { get; set; } = string.Empty;

    public string TargetHint { get; set; } = string.Empty;
}

public sealed class WorkerDecision
{
    public string Intent { get; set; } = "help";

    public string Goal { get; set; } = string.Empty;

    public string ReasoningSummary { get; set; } = string.Empty;

    public string PlanSummary { get; set; } = string.Empty;

    public string DecisionRationale { get; set; } = string.Empty;

    public string ResponseLead { get; set; } = string.Empty;

    public bool RequiresClarification { get; set; }

    public List<string> PlannedTools { get; set; } = new List<string>();

    public List<WorkerActionCard> SuggestedActions { get; set; } = new List<WorkerActionCard>();

    public string PreferredCommandId { get; set; } = string.Empty;

    public string ConfiguredProvider { get; set; } = string.Empty;

    public string PlannerModel { get; set; } = string.Empty;

    public string ResponseModel { get; set; } = string.Empty;

    public string ReasoningMode { get; set; } = WorkerReasoningModes.RuleFirst;
}
