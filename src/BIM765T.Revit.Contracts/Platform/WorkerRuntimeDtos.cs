using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace BIM765T.Revit.Contracts.Platform;

public static class WorkerClientSurfaces
{
    public const string Ui = "ui";
    public const string Mcp = "mcp";
}

public static class WorkerSessionStates
{
    public const string Active = "active";
    public const string Ended = "ended";
}

public static class WorkerMessageRoles
{
    public const string User = "user";
    public const string Worker = "worker";
    public const string System = "system";
    public const string Tool = "tool";
}

public static class WorkerMissionStates
{
    public const string Idle = "Idle";
    public const string Understanding = "Understanding";
    public const string Planned = "Planned";
    public const string AwaitingApproval = "AwaitingApproval";
    public const string Running = "Running";
    public const string Verifying = "Verifying";
    public const string Completed = "Completed";
    public const string Blocked = "Blocked";
    public const string Failed = "Failed";
}

public static class WorkerExecutionTiers
{
    public const string Tier0 = "tier0_read";
    public const string Tier1 = "tier1_mutate_low_risk";
    public const string Tier2 = "tier2_destructive";
}

public static class WorkerGroundingLevels
{
    public const string LiveContextOnly = "live_context_only";
    public const string WorkspaceGrounded = "workspace_grounded";
    public const string DeepScanGrounded = "deep_scan_grounded";
}

public static class WorkerStages
{
    public const string Intake = "intake";
    public const string Context = "context";
    public const string Planning = "planning";
    public const string Approval = "approval";
    public const string Execution = "execution";
    public const string Verification = "verification";
    public const string Recovery = "recovery";
    public const string Done = "done";
}

public static class WorkerFlowStages
{
    public const string Thinking = "thinking";
    public const string Plan = "plan";
    public const string Scan = "scan";
    public const string Preview = "preview";
    public const string Approval = "approval";
    public const string Run = "run";
    public const string Verify = "verify";
    public const string Done = "done";
    public const string Error = "error";

    public static string Normalize(string? stage)
    {
        var source = stage ?? string.Empty;
        if (string.IsNullOrWhiteSpace(source))
        {
            return Thinking;
        }

        var normalized = source.Trim().ToLowerInvariant();
        if (normalized == Thinking
            || normalized == WorkerStages.Intake
            || normalized == WorkerStages.Context)
        {
            return Thinking;
        }

        if (normalized == Plan || normalized == WorkerStages.Planning)
        {
            return Plan;
        }

        if (normalized == Scan)
        {
            return Scan;
        }

        if (normalized == Preview)
        {
            return Preview;
        }

        if (normalized == Approval || normalized == WorkerStages.Approval)
        {
            return Approval;
        }

        if (normalized == Run || normalized == WorkerStages.Execution)
        {
            return Run;
        }

        if (normalized == Verify || normalized == WorkerStages.Verification)
        {
            return Verify;
        }

        if (normalized == Done || normalized == WorkerStages.Done)
        {
            return Done;
        }

        if (normalized == Error || normalized == WorkerStages.Recovery)
        {
            return Error;
        }

        return normalized;
    }
}

public static class WorkerActionKinds
{
    public const string Suggest = "suggest";
    public const string Clarify = "ask_clarification";
    public const string Approve = "approve";
    public const string Reject = "reject";
    public const string Resume = "resume";
    public const string Context = "context";
    public const string Tool = "tool";
}

public static class WorkerSurfaceIds
{
    public const string Assistant = "assistant";
    public const string Commands = "commands";
    public const string Evidence = "evidence";
    public const string Activity = "activity";
}

public static class WorkerExecutionItemStates
{
    public const string Planned = "planned";
    public const string Running = "running";
    public const string AwaitingApproval = "awaiting_approval";
    public const string Completed = "completed";
    public const string Verified = "verified";
    public const string Failed = "failed";
}

public static class WorkerRiskLevels
{
    public const string None = "none";
    public const string ReadOnly = "read_only";
    public const string Low = "low";
    public const string Moderate = "moderate";
    public const string High = "high";
}

public static class WorkerReasoningModes
{
    public const string RuleFirst = "rule_first";
    public const string LlmValidated = "llm_validated";
}

public static class WorkerAutonomyModes
{
    public const string Bounded = "bounded";
    public const string Ship = "ship";

    public static string Normalize(string? mode)
    {
        var source = mode ?? string.Empty;
        if (string.IsNullOrWhiteSpace(source))
        {
            return Bounded;
        }

        return string.Equals(source.Trim(), Ship, StringComparison.OrdinalIgnoreCase)
            ? Ship
            : Bounded;
    }
}

public static class WorkerNarrationModes
{
    public const string RuleOnly = "rule_only";
    public const string LlmEnhanced = "llm_enhanced";
    public const string LlmFallback = "llm_fallback";
}

public static class WorkerMemoryKinds
{
    public const string UserMessage = "user_message";
    public const string WorkerResponse = "worker_response";
    public const string ToolResult = "tool_result";
    public const string ApprovalDecision = "approval_decision";
    public const string MissionSummary = "mission_summary";
    public const string RecoveryHint = "recovery_hint";
}

[DataContract]
public sealed class WorkerMessageRequest
{
    [DataMember(Order = 1)]
    public string SessionId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string Message { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string PersonaId { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string ClientSurface { get; set; } = WorkerClientSurfaces.Ui;

    [DataMember(Order = 5)]
    public bool ContinueMission { get; set; } = true;

    [DataMember(Order = 6)]
    public string AutonomyMode { get; set; } = string.Empty;

    [DataMember(Order = 7)]
    public string PlanningSummary { get; set; } = string.Empty;

    [DataMember(Order = 8)]
    public string PlannerTraceSummary { get; set; } = string.Empty;

    [DataMember(Order = 9)]
    public string GroundingLevel { get; set; } = string.Empty;

    [DataMember(Order = 10)]
    public List<string> ChosenToolSequence { get; set; } = new List<string>();

    [DataMember(Order = 11)]
    public List<string> EvidenceRefs { get; set; } = new List<string>();
}

[DataContract]
public sealed class WorkerSessionRequest
{
    [DataMember(Order = 1)]
    public string SessionId { get; set; } = string.Empty;
}

[DataContract]
public sealed class WorkerListSessionsRequest
{
    [DataMember(Order = 1)]
    public int MaxResults { get; set; } = 20;

    [DataMember(Order = 2)]
    public bool IncludeEnded { get; set; }
}

[DataContract]
public sealed class WorkerSetPersonaRequest
{
    [DataMember(Order = 1)]
    public string SessionId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string PersonaId { get; set; } = string.Empty;
}

[DataContract]
public sealed class WorkerContextRequest
{
    [DataMember(Order = 1)]
    public string SessionId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public bool IncludeTaskContext { get; set; } = true;

    [DataMember(Order = 3)]
    public bool IncludeDeltaSummary { get; set; } = true;

    [DataMember(Order = 4)]
    public int MaxRecentOperations { get; set; } = 10;

    [DataMember(Order = 5)]
    public int MaxRecentEvents { get; set; } = 10;
}

[DataContract]
public sealed class WorkerChatMessage
{
    [DataMember(Order = 1)]
    public string MessageId { get; set; } = Guid.NewGuid().ToString("N");

    [DataMember(Order = 2)]
    public string Role { get; set; } = WorkerMessageRoles.Worker;

    [DataMember(Order = 3)]
    public string Content { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

    [DataMember(Order = 5)]
    public string ToolName { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public string StatusCode { get; set; } = string.Empty;
}

[DataContract]
public sealed class WorkerToolCard
{
    [DataMember(Order = 1)]
    public string ToolName { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string StatusCode { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public bool Succeeded { get; set; }

    [DataMember(Order = 4)]
    public string Summary { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public string PayloadJson { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public List<string> ArtifactRefs { get; set; } = new List<string>();

    [DataMember(Order = 7)]
    public string Stage { get; set; } = string.Empty;

    [DataMember(Order = 8)]
    public double Progress { get; set; }

    [DataMember(Order = 9)]
    public string WhyThisTool { get; set; } = string.Empty;

    [DataMember(Order = 10)]
    public double Confidence { get; set; }

    [DataMember(Order = 11)]
    public List<string> RecoveryHints { get; set; } = new List<string>();

    [DataMember(Order = 12)]
    public string ExecutionTier { get; set; } = WorkerExecutionTiers.Tier0;

    [DataMember(Order = 13)]
    public bool AutoExecutionEligible { get; set; }
}

[DataContract]
public sealed class WorkerActionCard
{
    [DataMember(Order = 1)]
    public string ActionId { get; set; } = Guid.NewGuid().ToString("N");

    [DataMember(Order = 2)]
    public string ActionKind { get; set; } = WorkerActionKinds.Suggest;

    [DataMember(Order = 3)]
    public string Title { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string Summary { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public string ToolName { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public string PayloadJson { get; set; } = string.Empty;

    [DataMember(Order = 7)]
    public bool RequiresApproval { get; set; }

    [DataMember(Order = 8)]
    public bool IsPrimary { get; set; }

    [DataMember(Order = 9)]
    public string ExecutionTier { get; set; } = WorkerExecutionTiers.Tier0;

    [DataMember(Order = 10)]
    public string WhyThisAction { get; set; } = string.Empty;

    [DataMember(Order = 11)]
    public double Confidence { get; set; }

    [DataMember(Order = 12)]
    public string RecoveryHint { get; set; } = string.Empty;

    [DataMember(Order = 13)]
    public bool AutoExecutionEligible { get; set; }
}

[DataContract]
public sealed class PendingApprovalRef
{
    [DataMember(Order = 1)]
    public string PendingActionId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string ToolName { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string Summary { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public DateTime? ExpiresUtc { get; set; }

    [DataMember(Order = 5)]
    public string ExecutionTier { get; set; } = WorkerExecutionTiers.Tier0;

    [DataMember(Order = 6)]
    public string RecoveryHint { get; set; } = string.Empty;

    [DataMember(Order = 7)]
    public bool AutoExecutionEligible { get; set; }

    [DataMember(Order = 8)]
    public string ExpectedContextJson { get; set; } = string.Empty;
}

[DataContract]
public sealed class WorkerContextSummary
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string DocumentTitle { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string ActiveViewKey { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string ActiveViewName { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public int SelectionCount { get; set; }

    [DataMember(Order = 6)]
    public string Summary { get; set; } = string.Empty;

    [DataMember(Order = 7)]
    public List<string> SuggestedNextTools { get; set; } = new List<string>();

    [DataMember(Order = 8)]
    public List<string> SimilarEpisodeHints { get; set; } = new List<string>();

    [DataMember(Order = 9)]
    public string QueueSummary { get; set; } = string.Empty;

    [DataMember(Order = 10)]
    public string WorkspaceId { get; set; } = string.Empty;

    [DataMember(Order = 11)]
    public string PackSummary { get; set; } = string.Empty;

    [DataMember(Order = 12)]
    public string ProjectSummary { get; set; } = string.Empty;

    [DataMember(Order = 13)]
    public string ProjectPrimaryModelStatus { get; set; } = string.Empty;

    [DataMember(Order = 14)]
    public List<string> ProjectTopRefs { get; set; } = new List<string>();

    [DataMember(Order = 15)]
    public List<string> ProjectPendingUnknowns { get; set; } = new List<string>();

    [DataMember(Order = 16)]
    public string GroundingLevel { get; set; } = WorkerGroundingLevels.LiveContextOnly;

    [DataMember(Order = 17)]
    public string GroundingSummary { get; set; } = string.Empty;

    [DataMember(Order = 18)]
    public List<string> GroundingRefs { get; set; } = new List<string>();
}

[DataContract]
public sealed class WorkerContextPill
{
    [DataMember(Order = 1)]
    public string Key { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string Label { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string Value { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string Icon { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public string Tone { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public string Tooltip { get; set; } = string.Empty;

    [DataMember(Order = 7)]
    public bool IsPrimary { get; set; }
}

[DataContract]
public sealed class WorkerExecutionItem
{
    [DataMember(Order = 1)]
    public string ItemId { get; set; } = Guid.NewGuid().ToString("N");

    [DataMember(Order = 2)]
    public string Title { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string Summary { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string Status { get; set; } = WorkerExecutionItemStates.Planned;

    [DataMember(Order = 5)]
    public string Stage { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public string ToolName { get; set; } = string.Empty;

    [DataMember(Order = 7)]
    public double Progress { get; set; }

    [DataMember(Order = 8)]
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

    [DataMember(Order = 9)]
    public string ExecutionTier { get; set; } = WorkerExecutionTiers.Tier0;

    [DataMember(Order = 10)]
    public List<string> ArtifactRefs { get; set; } = new List<string>();
}

[DataContract]
public sealed class WorkerEvidenceItem
{
    [DataMember(Order = 1)]
    public string ArtifactRef { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string Title { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string Summary { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string Status { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public string SourceToolName { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public string VerificationMode { get; set; } = string.Empty;

    [DataMember(Order = 7)]
    public bool Verified { get; set; }
}

[DataContract]
public sealed class WorkerCommandSuggestion
{
    [DataMember(Order = 1)]
    public string CommandId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string Label { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string Summary { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string ToolName { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public bool RequiresApproval { get; set; }

    [DataMember(Order = 6)]
    public bool IsPrimary { get; set; }

    [DataMember(Order = 7)]
    public string SurfaceId { get; set; } = WorkerSurfaceIds.Commands;
}

[DataContract]
public sealed class WorkerSurfaceHint
{
    [DataMember(Order = 1)]
    public string SurfaceId { get; set; } = WorkerSurfaceIds.Assistant;

    [DataMember(Order = 2)]
    public string Reason { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string Emphasis { get; set; } = string.Empty;
}

[DataContract]
public sealed class WorkerRiskSummary
{
    [DataMember(Order = 1)]
    public string RiskLevel { get; set; } = WorkerRiskLevels.None;

    [DataMember(Order = 2)]
    public string Label { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string Summary { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public bool RequiresApproval { get; set; }

    [DataMember(Order = 5)]
    public string VerificationMode { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public int AffectedElementCount { get; set; }

    [DataMember(Order = 7)]
    public string ExecutionTier { get; set; } = WorkerExecutionTiers.Tier0;
}

[DataContract]
public sealed class WorkerMission
{
    [DataMember(Order = 1)]
    public string MissionId { get; set; } = Guid.NewGuid().ToString("N");

    [DataMember(Order = 2)]
    public string Intent { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string Goal { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string Status { get; set; } = WorkerMissionStates.Idle;

    [DataMember(Order = 5)]
    public string PlanSummary { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public string ReasoningSummary { get; set; } = string.Empty;

    [DataMember(Order = 7)]
    public string DecisionRationale { get; set; } = string.Empty;

    [DataMember(Order = 8)]
    public List<string> PlannedTools { get; set; } = new List<string>();

    [DataMember(Order = 9)]
    public string PendingStep { get; set; } = string.Empty;

    [DataMember(Order = 10)]
    public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;

    [DataMember(Order = 11)]
    public string Stage { get; set; } = WorkerStages.Intake;

    [DataMember(Order = 12)]
    public double Confidence { get; set; }

    [DataMember(Order = 13)]
    public string SelectedPlaybookId { get; set; } = string.Empty;

    [DataMember(Order = 14)]
    public string WorkspaceId { get; set; } = string.Empty;

    [DataMember(Order = 15)]
    public string CapabilityDomain { get; set; } = CapabilityDomains.General;

    [DataMember(Order = 16)]
    public string PolicySummary { get; set; } = string.Empty;

    [DataMember(Order = 17)]
    public List<string> RecommendedSpecialistIds { get; set; } = new List<string>();

    [DataMember(Order = 18)]
    public string ConfiguredProvider { get; set; } = string.Empty;

    [DataMember(Order = 19)]
    public string PlannerModel { get; set; } = string.Empty;

    [DataMember(Order = 20)]
    public string ResponseModel { get; set; } = string.Empty;

    [DataMember(Order = 21)]
    public string ReasoningMode { get; set; } = WorkerReasoningModes.RuleFirst;

    [DataMember(Order = 22)]
    public string AutonomyMode { get; set; } = WorkerAutonomyModes.Bounded;

    [DataMember(Order = 23)]
    public string PlannerTraceSummary { get; set; } = string.Empty;

    [DataMember(Order = 24)]
    public List<string> ChosenToolSequence { get; set; } = new List<string>();
}

[DataContract]
public sealed class WorkerSessionSummary
{
    [DataMember(Order = 1)]
    public string SessionId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string PersonaId { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string Status { get; set; } = WorkerSessionStates.Active;

    [DataMember(Order = 4)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public string MissionId { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public string LastUserMessage { get; set; } = string.Empty;

    [DataMember(Order = 7)]
    public DateTime StartedUtc { get; set; } = DateTime.UtcNow;

    [DataMember(Order = 8)]
    public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;
}

[DataContract]
public sealed class WorkerPersonaSummary
{
    [DataMember(Order = 1)]
    public string PersonaId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string DisplayName { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string Tone { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public List<string> Expertise { get; set; } = new List<string>();

    [DataMember(Order = 5)]
    public List<string> Guardrails { get; set; } = new List<string>();

    [DataMember(Order = 6)]
    public string GreetingTemplate { get; set; } = string.Empty;
}

[DataContract]
public sealed class WorkerContextResponse
{
    [DataMember(Order = 1)]
    public string SessionId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string MissionId { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public TaskContextResponse TaskContext { get; set; } = new TaskContextResponse();

    [DataMember(Order = 4)]
    public ContextDeltaSummaryResponse DeltaSummary { get; set; } = new ContextDeltaSummaryResponse();

    [DataMember(Order = 5)]
    public QueueStateResponse QueueState { get; set; } = new QueueStateResponse();

    [DataMember(Order = 6)]
    public List<string> SimilarEpisodes { get; set; } = new List<string>();

    [DataMember(Order = 7)]
    public string WorkspaceId { get; set; } = string.Empty;

    [DataMember(Order = 8)]
    public string ProjectSummary { get; set; } = string.Empty;

    [DataMember(Order = 9)]
    public string ProjectBrief { get; set; } = string.Empty;

    [DataMember(Order = 10)]
    public string ProjectPrimaryModelStatus { get; set; } = string.Empty;

    [DataMember(Order = 11)]
    public List<string> ProjectTopRefs { get; set; } = new List<string>();

    [DataMember(Order = 12)]
    public List<string> ProjectPendingUnknowns { get; set; } = new List<string>();
}

[DataContract]
public sealed class WorkerResponse
{
    [DataMember(Order = 1)]
    public string SessionId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string MissionId { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string MissionStatus { get; set; } = WorkerMissionStates.Idle;

    [DataMember(Order = 4)]
    public List<WorkerChatMessage> Messages { get; set; } = new List<WorkerChatMessage>();

    [DataMember(Order = 5)]
    public List<WorkerActionCard> ActionCards { get; set; } = new List<WorkerActionCard>();

    [DataMember(Order = 6)]
    public PendingApprovalRef PendingApproval { get; set; } = new PendingApprovalRef();

    [DataMember(Order = 7)]
    public List<WorkerToolCard> ToolCards { get; set; } = new List<WorkerToolCard>();

    [DataMember(Order = 8)]
    public List<string> ArtifactRefs { get; set; } = new List<string>();

    [DataMember(Order = 9)]
    public WorkerContextSummary ContextSummary { get; set; } = new WorkerContextSummary();

    [DataMember(Order = 10)]
    public string ReasoningSummary { get; set; } = string.Empty;

    [DataMember(Order = 11)]
    public string PlanSummary { get; set; } = string.Empty;

    [DataMember(Order = 12)]
    public string Stage { get; set; } = WorkerStages.Intake;

    [DataMember(Order = 13)]
    public double Progress { get; set; }

    [DataMember(Order = 14)]
    public double Confidence { get; set; }

    [DataMember(Order = 15)]
    public List<string> RecoveryHints { get; set; } = new List<string>();

    [DataMember(Order = 16)]
    public string ExecutionTier { get; set; } = WorkerExecutionTiers.Tier0;

    [DataMember(Order = 17)]
    public bool AutoExecutionEligible { get; set; }

    [DataMember(Order = 18)]
    public QueueStateResponse QueueState { get; set; } = new QueueStateResponse();

    [DataMember(Order = 19)]
    public string WorkspaceId { get; set; } = string.Empty;

    [DataMember(Order = 20)]
    public PlaybookRecommendation SelectedPlaybook { get; set; } = new PlaybookRecommendation();

    [DataMember(Order = 21)]
    public PlaybookPreviewResponse PlaybookPreview { get; set; } = new PlaybookPreviewResponse();

    [DataMember(Order = 22)]
    public string StandardsSummary { get; set; } = string.Empty;

    [DataMember(Order = 23)]
    public string ResolvedCapabilityDomain { get; set; } = CapabilityDomains.General;

    [DataMember(Order = 24)]
    public string PolicySummary { get; set; } = string.Empty;

    [DataMember(Order = 25)]
    public List<CapabilitySpecialistDescriptor> RecommendedSpecialists { get; set; } = new List<CapabilitySpecialistDescriptor>();

    [DataMember(Order = 26)]
    public CompiledTaskPlan CompiledPlan { get; set; } = new CompiledTaskPlan();

    [DataMember(Order = 27)]
    public List<WorkerContextPill> ContextPills { get; set; } = new List<WorkerContextPill>();

    [DataMember(Order = 28)]
    public List<WorkerExecutionItem> ExecutionItems { get; set; } = new List<WorkerExecutionItem>();

    [DataMember(Order = 29)]
    public List<WorkerEvidenceItem> EvidenceItems { get; set; } = new List<WorkerEvidenceItem>();

    [DataMember(Order = 30)]
    public List<WorkerCommandSuggestion> SuggestedCommands { get; set; } = new List<WorkerCommandSuggestion>();

    [DataMember(Order = 31)]
    public WorkerRiskSummary PrimaryRiskSummary { get; set; } = new WorkerRiskSummary();

    [DataMember(Order = 32)]
    public WorkerSurfaceHint SurfaceHint { get; set; } = new WorkerSurfaceHint();

    [DataMember(Order = 33)]
    public OnboardingStatusDto OnboardingStatus { get; set; } = new OnboardingStatusDto();

    [DataMember(Order = 34)]
    public FallbackArtifactProposal FallbackProposal { get; set; } = new FallbackArtifactProposal();

    [DataMember(Order = 35)]
    public SkillCaptureProposal SkillCaptureProposal { get; set; } = new SkillCaptureProposal();

    [DataMember(Order = 36)]
    public ProjectPatternSnapshot ProjectPatternSnapshot { get; set; } = new ProjectPatternSnapshot();

    [DataMember(Order = 37)]
    public TemplateSynthesisProposal TemplateSynthesisProposal { get; set; } = new TemplateSynthesisProposal();

    [DataMember(Order = 38)]
    public List<DeltaSuggestion> DeltaSuggestions { get; set; } = new List<DeltaSuggestion>();

    [DataMember(Order = 39)]
    public string ConfiguredProvider { get; set; } = string.Empty;

    [DataMember(Order = 40)]
    public string PlannerModel { get; set; } = string.Empty;

    [DataMember(Order = 41)]
    public string ResponseModel { get; set; } = string.Empty;

    [DataMember(Order = 42)]
    public string ReasoningMode { get; set; } = WorkerReasoningModes.RuleFirst;

    [DataMember(Order = 43)]
    public string NarrationMode { get; set; } = WorkerNarrationModes.RuleOnly;

    [DataMember(Order = 44)]
    public string NarrationDiagnostics { get; set; } = string.Empty;

    [DataMember(Order = 45)]
    public string AutonomyMode { get; set; } = WorkerAutonomyModes.Bounded;

    [DataMember(Order = 46)]
    public string PlannerTraceSummary { get; set; } = string.Empty;

    [DataMember(Order = 47)]
    public List<string> ChosenToolSequence { get; set; } = new List<string>();
}

[DataContract]
public sealed class SessionMemoryEntry
{
    [DataMember(Order = 1)]
    public string EntryId { get; set; } = Guid.NewGuid().ToString("N");

    [DataMember(Order = 2)]
    public string Kind { get; set; } = WorkerMemoryKinds.WorkerResponse;

    [DataMember(Order = 3)]
    public string Content { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public List<string> Tags { get; set; } = new List<string>();

    [DataMember(Order = 5)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public string ViewKey { get; set; } = string.Empty;

    [DataMember(Order = 7)]
    public string MissionId { get; set; } = string.Empty;

    [DataMember(Order = 8)]
    public string ToolName { get; set; } = string.Empty;

    [DataMember(Order = 9)]
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

[DataContract]
public sealed class EpisodicRecord
{
    [DataMember(Order = 1)]
    public string EpisodeId { get; set; } = Guid.NewGuid().ToString("N");

    [DataMember(Order = 2)]
    public string RunId { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string MissionType { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string Outcome { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public List<string> KeyObservations { get; set; } = new List<string>();

    [DataMember(Order = 6)]
    public List<string> KeyDecisions { get; set; } = new List<string>();

    [DataMember(Order = 7)]
    public List<string> ToolSequence { get; set; } = new List<string>();

    [DataMember(Order = 8)]
    public List<string> ArtifactRefs { get; set; } = new List<string>();

    [DataMember(Order = 9)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 10)]
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
