using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using BIM765T.Revit.Contracts.Common;

namespace BIM765T.Revit.Contracts.Platform;

[DataContract]
public sealed class WorkflowDefinition
{
    [DataMember(Order = 1)]
    public string WorkflowName { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string DisplayName { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string Description { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string Category { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public bool SupportsApply { get; set; }

    [DataMember(Order = 6)]
    public bool RequiresApproval { get; set; }

    [DataMember(Order = 7)]
    public List<string> RiskTags { get; set; } = new List<string>();

    [DataMember(Order = 8)]
    public List<string> RulePackTags { get; set; } = new List<string>();

    [DataMember(Order = 9)]
    public string InputSchemaJson { get; set; } = string.Empty;

    [DataMember(Order = 10)]
    public List<WorkflowStep> Steps { get; set; } = new List<WorkflowStep>();
}

[DataContract]
public sealed class WorkflowStep
{
    [DataMember(Order = 1)]
    public string StepName { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string ToolName { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string Mode { get; set; } = "read";

    [DataMember(Order = 4)]
    public string Description { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public bool IsCheckpoint { get; set; }
}

[DataContract]
public sealed class WorkflowListResponse
{
    [DataMember(Order = 1)]
    public List<WorkflowDefinition> Workflows { get; set; } = new List<WorkflowDefinition>();
}

[DataContract]
public sealed class WorkflowPlanRequest
{
    [DataMember(Order = 1)]
    public string WorkflowName { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string InputJson { get; set; } = string.Empty;
}

[DataContract]
public sealed class WorkflowApplyRequest
{
    [DataMember(Order = 1)]
    public string RunId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string ApprovalToken { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public bool AllowMutations { get; set; }
}

[DataContract]
public sealed class WorkflowGetRunRequest
{
    [DataMember(Order = 1)]
    public string RunId { get; set; } = string.Empty;
}

[DataContract]
public sealed class WorkflowCheckpoint
{
    [DataMember(Order = 1)]
    public string StepName { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string Status { get; set; } = "pending";

    [DataMember(Order = 3)]
    public string Message { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

    [DataMember(Order = 5)]
    public List<int> ChangedIds { get; set; } = new List<int>();

    [DataMember(Order = 6)]
    public List<string> ArtifactKeys { get; set; } = new List<string>();
}

[DataContract]
public sealed class WorkflowEvidenceBundle
{
    [DataMember(Order = 1)]
    public string RunId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string PlanSummary { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public List<string> ArtifactKeys { get; set; } = new List<string>();

    [DataMember(Order = 4)]
    public List<string> SnapshotPayloads { get; set; } = new List<string>();

    [DataMember(Order = 5)]
    public List<string> ReviewPayloads { get; set; } = new List<string>();

    [DataMember(Order = 6)]
    public List<string> ResultPayloads { get; set; } = new List<string>();
}

[DataContract]
public sealed class WorkflowRun
{
    [DataMember(Order = 1)]
    public string RunId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string WorkflowName { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string Status { get; set; } = "planned";

    [DataMember(Order = 4)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public ContextFingerprint Fingerprint { get; set; } = new ContextFingerprint();

    [DataMember(Order = 6)]
    public string InputJson { get; set; } = string.Empty;

    [DataMember(Order = 7)]
    public bool RequiresApproval { get; set; }

    [DataMember(Order = 8)]
    public string ApprovalToken { get; set; } = string.Empty;

    [DataMember(Order = 9)]
    public string PreviewRunId { get; set; } = string.Empty;

    [DataMember(Order = 10)]
    public string MutationToolName { get; set; } = string.Empty;

    [DataMember(Order = 11)]
    public string MutationPayloadJson { get; set; } = string.Empty;

    [DataMember(Order = 12)]
    public string ExpectedContextJson { get; set; } = string.Empty;

    [DataMember(Order = 13)]
    public string Caller { get; set; } = string.Empty;

    [DataMember(Order = 14)]
    public string SessionId { get; set; } = string.Empty;

    [DataMember(Order = 15)]
    public DateTime PlannedUtc { get; set; } = DateTime.UtcNow;

    [DataMember(Order = 16)]
    public DateTime? AppliedUtc { get; set; }

    [DataMember(Order = 17)]
    public List<WorkflowCheckpoint> Checkpoints { get; set; } = new List<WorkflowCheckpoint>();

    [DataMember(Order = 18)]
    public WorkflowEvidenceBundle Evidence { get; set; } = new WorkflowEvidenceBundle();

    [DataMember(Order = 19)]
    public List<DiagnosticRecord> Diagnostics { get; set; } = new List<DiagnosticRecord>();

    [DataMember(Order = 20)]
    public List<int> ChangedIds { get; set; } = new List<int>();

    [DataMember(Order = 21)]
    public long DurationMs { get; set; }
}
