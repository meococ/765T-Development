using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Agent.UI.Chat;

internal static class TimelineEntryKinds
{
    public const string UserMessage = "user_message";
    public const string AssistantMessage = "assistant_message";
    public const string SystemStateTurn = "system_state_turn";
    public const string MissionTraceTurn = "mission_trace_turn";
    public const string ArtifactRow = "artifact_row";
}

internal static class SystemTurnKinds
{
    public const string Approval = "approval";
    public const string Fallback = "fallback";
    public const string Onboarding = "onboarding";
    public const string Error = "error";
}

internal static class SystemTurnActionKinds
{
    public const string Approve = "approve";
    public const string Reject = "reject";
    public const string Resume = "resume";
    public const string InitWorkspace = "init_workspace";
    public const string RunDeepScan = "run_deep_scan";
    public const string OpenArtifact = "open_artifact";
    public const string CopyPath = "copy_path";
    public const string ApplyInRevit = "apply_in_revit";
}

internal sealed class ChatSessionVm
{
    public string SessionId { get; set; } = string.Empty;

    public string MissionId { get; set; } = string.Empty;

    public bool IsBusy { get; set; }

    public WorkerResponse LatestWorkerResponse { get; set; } = new WorkerResponse();

    public WorkerHostMissionResponse LatestMissionResponse { get; set; } = new WorkerHostMissionResponse();

    public List<TimelineEntryVm> Entries { get; } = new List<TimelineEntryVm>();
}

internal sealed class TimelineEntryVm
{
    public string EntryId { get; set; } = Guid.NewGuid().ToString("N");

    public string Kind { get; set; } = TimelineEntryKinds.AssistantMessage;

    public WorkerChatMessage Message { get; set; } = new WorkerChatMessage();

    public MissionTraceVm Trace { get; set; } = new MissionTraceVm();

    public SystemStateTurnVm SystemTurn { get; set; } = new SystemStateTurnVm();

    public ArtifactAttachmentVm Artifact { get; set; } = new ArtifactAttachmentVm();
}

internal sealed class MissionTraceVm
{
    public string MissionId { get; set; } = string.Empty;

    public string Title { get; set; } = "Mission Trace";

    public string Summary { get; set; } = string.Empty;

    public string State { get; set; } = WorkerMissionStates.Idle;

    public string Stage { get; set; } = WorkerFlowStages.Thinking;

    public bool IsExpanded { get; set; }

    public bool IsTerminal { get; set; }

    public string ReasoningMode { get; set; } = WorkerReasoningModes.RuleFirst;

    public List<string> Badges { get; } = new List<string>();

    public List<MissionTraceEventVm> Events { get; } = new List<MissionTraceEventVm>();
}

internal sealed class MissionTraceEventVm
{
    public long Version { get; set; }

    public string EventType { get; set; } = string.Empty;

    public string OccurredUtc { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public string AccentKind { get; set; } = string.Empty;
}

internal sealed class SystemStateTurnVm
{
    public string TurnKind { get; set; } = SystemTurnKinds.Onboarding;

    public string Title { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public List<string> Badges { get; } = new List<string>();

    public List<SystemTurnActionVm> Actions { get; } = new List<SystemTurnActionVm>();
}

internal sealed class SystemTurnActionVm
{
    public string ActionKind { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public string CommandText { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;
}

internal sealed class ArtifactAttachmentVm
{
    public string Label { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public string Source { get; set; } = string.Empty;
}

[DataContract]
internal sealed class WorkerHostChatRequest
{
    [DataMember(Order = 1)]
    public string MissionId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string SessionId { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string Message { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string PersonaId { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public bool ContinueMission { get; set; } = true;

    [DataMember(Order = 6)]
    public string ActorId { get; set; } = "revit-ui";
}

[DataContract]
internal sealed class WorkerHostMissionCommandRequest
{
    [DataMember(Order = 1)]
    public string SessionId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string ApprovalToken { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string PreviewRunId { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string Note { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public string ActorId { get; set; } = "revit-ui";
}

[DataContract]
internal sealed class WorkerHostMissionResponse
{
    [DataMember(Order = 1)]
    public string MissionId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string SessionId { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string State { get; set; } = WorkerMissionStates.Idle;

    [DataMember(Order = 4)]
    public bool Succeeded { get; set; }

    [DataMember(Order = 5)]
    public string StatusCode { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public string ResponseText { get; set; } = string.Empty;

    [DataMember(Order = 7)]
    public string PayloadJson { get; set; } = string.Empty;

    [DataMember(Order = 8)]
    public string ApprovalToken { get; set; } = string.Empty;

    [DataMember(Order = 9)]
    public string PreviewRunId { get; set; } = string.Empty;

    [DataMember(Order = 10)]
    public bool HasPendingApproval { get; set; }

    [DataMember(Order = 11)]
    public string PendingActionId { get; set; } = string.Empty;

    [DataMember(Order = 12)]
    public string SuggestedSurface { get; set; } = string.Empty;

    [DataMember(Order = 13)]
    public List<string> ArtifactRefs { get; set; } = new List<string>();

    [DataMember(Order = 14)]
    public List<string> Diagnostics { get; set; } = new List<string>();

    [DataMember(Order = 15)]
    public string ConfiguredProvider { get; set; } = string.Empty;

    [DataMember(Order = 16)]
    public string PlannerModel { get; set; } = string.Empty;

    [DataMember(Order = 17)]
    public string ResponseModel { get; set; } = string.Empty;

    [DataMember(Order = 18)]
    public string ReasoningMode { get; set; } = WorkerReasoningModes.RuleFirst;

    [DataMember(Order = 19)]
    public List<WorkerHostMissionEvent> Events { get; set; } = new List<WorkerHostMissionEvent>();

    [DataMember(Order = 20)]
    public string FlowState { get; set; } = string.Empty;

    [DataMember(Order = 21)]
    public string GroundingLevel { get; set; } = string.Empty;

    [DataMember(Order = 22)]
    public string PlanningSummary { get; set; } = string.Empty;

    [DataMember(Order = 23)]
    public string PlannerTraceSummary { get; set; } = string.Empty;

    [DataMember(Order = 24)]
    public bool ApprovalRequired { get; set; }

    [DataMember(Order = 25)]
    public List<string> ChosenToolSequence { get; set; } = new List<string>();

    [DataMember(Order = 26)]
    public List<string> EvidenceRefs { get; set; } = new List<string>();

    [DataMember(Order = 27)]
    public string AutonomyMode { get; set; } = WorkerAutonomyModes.Bounded;
}

[DataContract]
internal sealed class WorkerHostMissionEvent
{
    [DataMember(Order = 1)]
    public long Version { get; set; }

    [DataMember(Order = 2)]
    public string EventType { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string OccurredUtc { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string PayloadJson { get; set; } = "{}";

    [DataMember(Order = 5)]
    public bool Terminal { get; set; }
}

[DataContract]
internal sealed class WorkerHostGatewayStatus
{
    [DataMember(Order = 1)]
    public SessionRuntimeHealthResponse Health { get; set; } = new SessionRuntimeHealthResponse();

    [DataMember(Order = 2)]
    public string ConfiguredProvider { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string PlannerModel { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string ResponseModel { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public string ReasoningMode { get; set; } = WorkerReasoningModes.RuleFirst;

    [DataMember(Order = 6)]
    public string SecretSourceKind { get; set; } = string.Empty;
}
