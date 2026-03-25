using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using BIM765T.Revit.Contracts.Common;

namespace BIM765T.Revit.Contracts.Platform;

[DataContract]
public sealed class TaskStepState
{
    [DataMember(Order = 1)]
    public string StepId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string Title { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string StepKind { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string Status { get; set; } = "pending";

    [DataMember(Order = 5)]
    public bool RequiresApproval { get; set; }

    [DataMember(Order = 6)]
    public string DecisionReason { get; set; } = string.Empty;

    [DataMember(Order = 7)]
    public int ExpectedDelta { get; set; }

    [DataMember(Order = 8)]
    public int ActualDelta { get; set; }

    [DataMember(Order = 9)]
    public List<string> ArtifactKeys { get; set; } = new List<string>();

    [DataMember(Order = 10)]
    public List<int> ChangedIds { get; set; } = new List<int>();

    [DataMember(Order = 11)]
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}

[DataContract]
public sealed class TaskCheckpointRecord
{
    [DataMember(Order = 1)]
    public string CheckpointId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string StepId { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string Status { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string Summary { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public string ReasonCode { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public string ReasonMessage { get; set; } = string.Empty;

    [DataMember(Order = 7)]
    public string NextAction { get; set; } = string.Empty;

    [DataMember(Order = 8)]
    public bool CanResume { get; set; }

    [DataMember(Order = 9)]
    public List<string> ArtifactKeys { get; set; } = new List<string>();

    [DataMember(Order = 10)]
    public List<int> ChangedIds { get; set; } = new List<int>();

    [DataMember(Order = 11)]
    public int ExpectedDelta { get; set; }

    [DataMember(Order = 12)]
    public int ActualDelta { get; set; }

    [DataMember(Order = 13)]
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

[DataContract]
public sealed class TaskRecoveryBranch
{
    [DataMember(Order = 1)]
    public string BranchId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string Title { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string Description { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string NextAction { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public string ReasonCode { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public bool RequiresApproval { get; set; }

    [DataMember(Order = 7)]
    public bool RequiresFreshPreview { get; set; }

    [DataMember(Order = 8)]
    public bool AutoResumable { get; set; }

    [DataMember(Order = 9)]
    public bool IsRecommended { get; set; }
}

[DataContract]
public sealed class TaskRun
{
    [DataMember(Order = 1)]
    public string RunId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string TaskKind { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string TaskName { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string Status { get; set; } = "planned";

    [DataMember(Order = 5)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public string IntentSummary { get; set; } = string.Empty;

    [DataMember(Order = 7)]
    public string PlanSummary { get; set; } = string.Empty;

    [DataMember(Order = 8)]
    public string InputJson { get; set; } = "{}";

    [DataMember(Order = 9)]
    public string UnderlyingRunId { get; set; } = string.Empty;

    [DataMember(Order = 10)]
    public string UnderlyingKind { get; set; } = string.Empty;

    [DataMember(Order = 11)]
    public string ExpectedContextJson { get; set; } = string.Empty;

    [DataMember(Order = 12)]
    public string ApprovalToken { get; set; } = string.Empty;

    [DataMember(Order = 13)]
    public string PreviewRunId { get; set; } = string.Empty;

    [DataMember(Order = 14)]
    public string PlannedByCaller { get; set; } = string.Empty;

    [DataMember(Order = 15)]
    public string PlannedBySessionId { get; set; } = string.Empty;

    [DataMember(Order = 16)]
    public string ApprovedByCaller { get; set; } = string.Empty;

    [DataMember(Order = 17)]
    public string ApprovedBySessionId { get; set; } = string.Empty;

    [DataMember(Order = 18)]
    public string ApprovalNote { get; set; } = string.Empty;

    [DataMember(Order = 19)]
    public List<string> RecommendedActionIds { get; set; } = new List<string>();

    [DataMember(Order = 20)]
    public List<string> SelectedActionIds { get; set; } = new List<string>();

    [DataMember(Order = 21)]
    public List<TaskStepState> Steps { get; set; } = new List<TaskStepState>();

    [DataMember(Order = 22)]
    public List<DiagnosticRecord> Diagnostics { get; set; } = new List<DiagnosticRecord>();

    [DataMember(Order = 23)]
    public List<int> ChangedIds { get; set; } = new List<int>();

    [DataMember(Order = 24)]
    public List<string> ArtifactKeys { get; set; } = new List<string>();

    [DataMember(Order = 25)]
    public List<string> Tags { get; set; } = new List<string>();

    [DataMember(Order = 26)]
    public int ExpectedDelta { get; set; }

    [DataMember(Order = 27)]
    public int ActualDelta { get; set; }

    [DataMember(Order = 28)]
    public string VerificationStatus { get; set; } = string.Empty;

    [DataMember(Order = 29)]
    public string ResidualSummary { get; set; } = string.Empty;

    [DataMember(Order = 30)]
    public long DurationMs { get; set; }

    [DataMember(Order = 31)]
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    [DataMember(Order = 32)]
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    [DataMember(Order = 33)]
    public DateTime? VerifiedUtc { get; set; }

    [DataMember(Order = 34)]
    public List<TaskCheckpointRecord> Checkpoints { get; set; } = new List<TaskCheckpointRecord>();

    [DataMember(Order = 35)]
    public List<TaskRecoveryBranch> RecoveryBranches { get; set; } = new List<TaskRecoveryBranch>();

    [DataMember(Order = 36)]
    public string LastErrorCode { get; set; } = string.Empty;

    [DataMember(Order = 37)]
    public string LastErrorMessage { get; set; } = string.Empty;

    [DataMember(Order = 38)]
    public TaskSpec TaskSpec { get; set; } = new TaskSpec();

    [DataMember(Order = 39)]
    public WorkerProfile WorkerProfile { get; set; } = new WorkerProfile();

    [DataMember(Order = 40)]
    public RunReport RunReport { get; set; } = new RunReport();

    [DataMember(Order = 41)]
    public string CapabilityPack { get; set; } = WorkerCapabilityPacks.CoreWorker;

    [DataMember(Order = 42)]
    public string PrimarySkillGroup { get; set; } = WorkerSkillGroups.Orchestration;

    [DataMember(Order = 43)]
    public bool QueueEligible { get; set; }

    [DataMember(Order = 44)]
    public ConnectorTaskEnvelope ConnectorTask { get; set; } = new ConnectorTaskEnvelope();

    [DataMember(Order = 45)]
    public string LastQueueItemId { get; set; } = string.Empty;
}

[DataContract]
public sealed class TaskListResponse
{
    [DataMember(Order = 1)]
    public List<TaskRun> Runs { get; set; } = new List<TaskRun>();
}

[DataContract]
public sealed class TaskSummaryResponse
{
    [DataMember(Order = 1)]
    public string RunId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string TaskKind { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string TaskName { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string Status { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public string IntentSummary { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public string PlanSummary { get; set; } = string.Empty;

    [DataMember(Order = 7)]
    public string NextAction { get; set; } = string.Empty;

    [DataMember(Order = 8)]
    public int ChangedCount { get; set; }

    [DataMember(Order = 9)]
    public int ResidualCount { get; set; }

    [DataMember(Order = 10)]
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    [DataMember(Order = 11)]
    public int CheckpointCount { get; set; }

    [DataMember(Order = 12)]
    public int RecoveryBranchCount { get; set; }

    [DataMember(Order = 13)]
    public bool CanResume { get; set; }

    [DataMember(Order = 14)]
    public string LastErrorCode { get; set; } = string.Empty;

    [DataMember(Order = 15)]
    public string CapabilityPack { get; set; } = string.Empty;

    [DataMember(Order = 16)]
    public string PrimarySkillGroup { get; set; } = string.Empty;

    [DataMember(Order = 17)]
    public string WorkerPersonaId { get; set; } = string.Empty;

    [DataMember(Order = 18)]
    public RunReport RunReport { get; set; } = new RunReport();
}

[DataContract]
public sealed class TaskMetricRow
{
    [DataMember(Order = 1)]
    public string TaskKind { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string TaskName { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public int TotalRuns { get; set; }

    [DataMember(Order = 5)]
    public int CompletedRuns { get; set; }

    [DataMember(Order = 6)]
    public int VerifiedPassRuns { get; set; }

    [DataMember(Order = 7)]
    public double AverageDurationMs { get; set; }

    [DataMember(Order = 8)]
    public double AverageChangedCount { get; set; }

    [DataMember(Order = 9)]
    public double AverageResidualCount { get; set; }
}

[DataContract]
public sealed class TaskMetricsResponse
{
    [DataMember(Order = 1)]
    public List<TaskMetricRow> Metrics { get; set; } = new List<TaskMetricRow>();
}

[DataContract]
public sealed class TaskResidualsResponse
{
    [DataMember(Order = 1)]
    public string RunId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string VerificationStatus { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string ResidualSummary { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public List<DiagnosticRecord> Diagnostics { get; set; } = new List<DiagnosticRecord>();

    [DataMember(Order = 5)]
    public List<TaskRecoveryBranch> RecoveryBranches { get; set; } = new List<TaskRecoveryBranch>();

    [DataMember(Order = 6)]
    public string LastErrorCode { get; set; } = string.Empty;

    [DataMember(Order = 7)]
    public string LastErrorMessage { get; set; } = string.Empty;
}

[DataContract]
public sealed class QueueStateResponse
{
    [DataMember(Order = 1)]
    public int PendingCount { get; set; }

    [DataMember(Order = 2)]
    public bool HasActiveInvocation { get; set; }

    [DataMember(Order = 3)]
    public string ActiveToolName { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string ActiveRequestId { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public DateTime? ActiveStartedUtc { get; set; }

    [DataMember(Order = 6)]
    public int PendingHighPriorityCount { get; set; }

    [DataMember(Order = 7)]
    public int PendingNormalPriorityCount { get; set; }

    [DataMember(Order = 8)]
    public int PendingLowPriorityCount { get; set; }

    [DataMember(Order = 9)]
    public string ActiveStage { get; set; } = string.Empty;

    [DataMember(Order = 10)]
    public string ActiveExecutionTier { get; set; } = WorkerExecutionTiers.Tier0;

    [DataMember(Order = 11)]
    public string ActiveRiskTier { get; set; } = ToolRiskTiers.Tier0;

    [DataMember(Order = 12)]
    public string ActiveLatencyClass { get; set; } = ToolLatencyClasses.Standard;

    [DataMember(Order = 13)]
    public DateTime? HeartbeatUtc { get; set; }

    [DataMember(Order = 14)]
    public long ActiveElapsedMs { get; set; }

    [DataMember(Order = 15)]
    public bool CanCancelPending { get; set; }
}

[DataContract]
public sealed class DocumentStateNode
{
    [DataMember(Order = 1)]
    public string NodeId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string Kind { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string Label { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public int? ElementId { get; set; }
}

[DataContract]
public sealed class DocumentStateEdge
{
    [DataMember(Order = 1)]
    public string FromNodeId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string ToNodeId { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string Relation { get; set; } = string.Empty;
}

[DataContract]
public sealed class DocumentStateGraphSnapshot
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string ViewKey { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public int ActiveDocEpoch { get; set; }

    [DataMember(Order = 4)]
    public DateTime RefreshedUtc { get; set; } = DateTime.UtcNow;

    [DataMember(Order = 5)]
    public int ElementCountEstimate { get; set; }

    [DataMember(Order = 6)]
    public int FamilyInstanceCountEstimate { get; set; }

    [DataMember(Order = 7)]
    public int SelectionCount { get; set; }

    [DataMember(Order = 8)]
    public List<int> RecentChangedIds { get; set; } = new List<int>();

    [DataMember(Order = 9)]
    public List<DocumentStateNode> Nodes { get; set; } = new List<DocumentStateNode>();

    [DataMember(Order = 10)]
    public List<DocumentStateEdge> Edges { get; set; } = new List<DocumentStateEdge>();
}

[DataContract]
public sealed class SessionRuntimeHealthResponse
{
    [DataMember(Order = 1)]
    public string RuntimeMode { get; set; } = "embedded_agent";

    [DataMember(Order = 2)]
    public bool SupportsTaskRuntime { get; set; } = true;

    [DataMember(Order = 3)]
    public bool SupportsContextBroker { get; set; } = true;

    [DataMember(Order = 4)]
    public bool SupportsStateGraph { get; set; } = true;

    [DataMember(Order = 5)]
    public bool SupportsDurableTaskRuns { get; set; } = true;

    [DataMember(Order = 6)]
    public string StateRootPath { get; set; } = string.Empty;

    [DataMember(Order = 7)]
    public int DurableRunCount { get; set; }

    [DataMember(Order = 8)]
    public int PromotionCount { get; set; }

    [DataMember(Order = 9)]
    public int ToolCount { get; set; }

    [DataMember(Order = 10)]
    public QueueStateResponse Queue { get; set; } = new QueueStateResponse();

    [DataMember(Order = 11)]
    public List<string> SupportedTaskKinds { get; set; } = new List<string>();

    [DataMember(Order = 12)]
    public bool SupportsCheckpointRecovery { get; set; } = true;

    [DataMember(Order = 13)]
    public List<string> EnabledCapabilityPacks { get; set; } = new List<string>();

    [DataMember(Order = 14)]
    public WorkerProfile DefaultWorkerProfile { get; set; } = new WorkerProfile();

    [DataMember(Order = 15)]
    public string VisibleShellMode { get; set; } = WorkerShellModes.Worker;

    [DataMember(Order = 16)]
    public int DurableQueuePendingCount { get; set; }

    [DataMember(Order = 17)]
    public int DurableQueueLeasedCount { get; set; }

    [DataMember(Order = 18)]
    public string ConfiguredProvider { get; set; } = string.Empty;

    [DataMember(Order = 19)]
    public string PlannerModel { get; set; } = string.Empty;

    [DataMember(Order = 20)]
    public string ResponseModel { get; set; } = string.Empty;

    [DataMember(Order = 21)]
    public string ReasoningMode { get; set; } = WorkerReasoningModes.RuleFirst;

    [DataMember(Order = 22)]
    public string SecretSourceKind { get; set; } = string.Empty;

    [DataMember(Order = 23)]
    public string LoadedAssemblyPath { get; set; } = string.Empty;

    [DataMember(Order = 24)]
    public string ConfiguredAssemblyPath { get; set; } = string.Empty;

    [DataMember(Order = 25)]
    public bool RestartRequired { get; set; }

    [DataMember(Order = 26)]
    public List<string> RuntimeWarnings { get; set; } = new List<string>();
}
