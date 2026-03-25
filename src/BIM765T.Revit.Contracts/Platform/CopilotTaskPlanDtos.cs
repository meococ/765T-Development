using System.Collections.Generic;
using System.Runtime.Serialization;
using BIM765T.Revit.Contracts.Common;

namespace BIM765T.Revit.Contracts.Platform;

[DataContract]
public sealed class TaskPlanRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string TaskKind { get; set; } = "fix_loop";

    [DataMember(Order = 3)]
    public string TaskName { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string IntentSummary { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public string InputJson { get; set; } = "{}";

    [DataMember(Order = 6)]
    public List<string> Tags { get; set; } = new List<string>();

    [DataMember(Order = 7)]
    public TaskSpec TaskSpec { get; set; } = new TaskSpec();

    [DataMember(Order = 8)]
    public WorkerProfile WorkerProfile { get; set; } = new WorkerProfile();

    [DataMember(Order = 9)]
    public string PreferredCapabilityPack { get; set; } = string.Empty;

    [DataMember(Order = 10)]
    public ConnectorTaskEnvelope ConnectorTask { get; set; } = new ConnectorTaskEnvelope();
}

[DataContract]
public sealed class TaskPreviewRequest
{
    [DataMember(Order = 1)]
    public string RunId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string StepId { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public List<string> ActionIds { get; set; } = new List<string>();
}

[DataContract]
public sealed class TaskApproveStepRequest
{
    [DataMember(Order = 1)]
    public string RunId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string StepId { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string ApprovalToken { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string PreviewRunId { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public string Note { get; set; } = string.Empty;
}

[DataContract]
public sealed class TaskExecuteStepRequest
{
    [DataMember(Order = 1)]
    public string RunId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string StepId { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public bool AllowMutations { get; set; } = true;
}

[DataContract]
public sealed class TaskResumeRequest
{
    [DataMember(Order = 1)]
    public string RunId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public bool AllowMutations { get; set; } = true;

    [DataMember(Order = 3)]
    public string RecoveryBranchId { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public int MaxResidualIssues { get; set; } = 200;
}

[DataContract]
public sealed class TaskVerifyRequest
{
    [DataMember(Order = 1)]
    public string RunId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public int MaxResidualIssues { get; set; } = 200;
}

[DataContract]
public sealed class TaskGetRunRequest
{
    [DataMember(Order = 1)]
    public string RunId { get; set; } = string.Empty;
}

[DataContract]
public sealed class TaskListRunsRequest
{
    [DataMember(Order = 1)]
    public string TaskKind { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string TaskName { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string Status { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public int MaxResults { get; set; } = 50;
}

[DataContract]
public sealed class TaskSummarizeRequest
{
    [DataMember(Order = 1)]
    public string RunId { get; set; } = string.Empty;
}

[DataContract]
public sealed class ExternalTaskIntakeRequest
{
    [DataMember(Order = 1)]
    public ConnectorTaskEnvelope Envelope { get; set; } = new ConnectorTaskEnvelope();

    [DataMember(Order = 2)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string TaskKind { get; set; } = "workflow";

    [DataMember(Order = 4)]
    public string TaskName { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public string IntentSummary { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public string InputJson { get; set; } = "{}";

    [DataMember(Order = 7)]
    public List<string> Tags { get; set; } = new List<string>();

    [DataMember(Order = 8)]
    public TaskSpec TaskSpec { get; set; } = new TaskSpec();

    [DataMember(Order = 9)]
    public WorkerProfile WorkerProfile { get; set; } = new WorkerProfile();

    [DataMember(Order = 10)]
    public string PreferredCapabilityPack { get; set; } = string.Empty;
}

[DataContract]
public sealed class ConnectorTaskIntakeResponse
{
    [DataMember(Order = 1)]
    public TaskRun CreatedRun { get; set; } = new TaskRun();

    [DataMember(Order = 2)]
    public TaskPlanRequest NormalizedRequest { get; set; } = new TaskPlanRequest();

    [DataMember(Order = 3)]
    public string ConnectorSummary { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public bool CallbackPreviewAvailable { get; set; }
}
