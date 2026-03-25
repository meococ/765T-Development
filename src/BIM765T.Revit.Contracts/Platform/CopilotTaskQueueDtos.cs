using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace BIM765T.Revit.Contracts.Platform;

[DataContract]
public sealed class TaskQueueEnqueueRequest
{
    [DataMember(Order = 1)]
    public string RunId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string QueueName { get; set; } = "approved";

    [DataMember(Order = 3)]
    public DateTime? ScheduledUtc { get; set; }

    [DataMember(Order = 4)]
    public string Note { get; set; } = string.Empty;
}

[DataContract]
public sealed class TaskQueueClaimRequest
{
    [DataMember(Order = 1)]
    public string QueueName { get; set; } = "approved";

    [DataMember(Order = 2)]
    public string LeaseOwner { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public bool IncludeScheduledFuture { get; set; }
}

[DataContract]
public sealed class TaskQueueCompleteRequest
{
    [DataMember(Order = 1)]
    public string QueueItemId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string Status { get; set; } = "completed";

    [DataMember(Order = 3)]
    public string Message { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public List<string> ArtifactKeys { get; set; } = new List<string>();
}

[DataContract]
public sealed class TaskQueueRunRequest
{
    [DataMember(Order = 1)]
    public string QueueItemId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string LeaseOwner { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public bool AllowMutations { get; set; } = true;

    [DataMember(Order = 4)]
    public bool AutoVerify { get; set; } = true;

    [DataMember(Order = 5)]
    public int MaxResidualIssues { get; set; } = 200;

    [DataMember(Order = 6)]
    public string RecoveryBranchId { get; set; } = string.Empty;

    [DataMember(Order = 7)]
    public string ResultStatusOverride { get; set; } = string.Empty;
}

[DataContract]
public sealed class TaskQueueListRequest
{
    [DataMember(Order = 1)]
    public string QueueName { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string Status { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string RunId { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string ConnectorSystem { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public int MaxResults { get; set; } = 50;

    [DataMember(Order = 6)]
    public bool IncludeCompleted { get; set; }
}

[DataContract]
public sealed class TaskQueueItem
{
    [DataMember(Order = 1)]
    public string QueueItemId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string RunId { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string TaskKind { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string TaskName { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public string QueueName { get; set; } = "approved";

    [DataMember(Order = 7)]
    public string Status { get; set; } = "pending";

    [DataMember(Order = 8)]
    public string ConnectorSystem { get; set; } = string.Empty;

    [DataMember(Order = 9)]
    public string ExternalTaskRef { get; set; } = string.Empty;

    [DataMember(Order = 10)]
    public string CallbackMode { get; set; } = "panel_only";

    [DataMember(Order = 11)]
    public string ApprovalToken { get; set; } = string.Empty;

    [DataMember(Order = 12)]
    public string PreviewRunId { get; set; } = string.Empty;

    [DataMember(Order = 13)]
    public string EnqueuedByCaller { get; set; } = string.Empty;

    [DataMember(Order = 14)]
    public string LeaseOwner { get; set; } = string.Empty;

    [DataMember(Order = 15)]
    public string Note { get; set; } = string.Empty;

    [DataMember(Order = 16)]
    public string LastStatusMessage { get; set; } = string.Empty;

    [DataMember(Order = 17)]
    public List<string> ArtifactKeys { get; set; } = new List<string>();

    [DataMember(Order = 18)]
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    [DataMember(Order = 19)]
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    [DataMember(Order = 20)]
    public DateTime? ScheduledUtc { get; set; }

    [DataMember(Order = 21)]
    public DateTime? LeasedUtc { get; set; }

    [DataMember(Order = 22)]
    public DateTime? CompletedUtc { get; set; }
}

[DataContract]
public sealed class TaskQueueListResponse
{
    [DataMember(Order = 1)]
    public List<TaskQueueItem> Items { get; set; } = new List<TaskQueueItem>();
}

[DataContract]
public sealed class TaskQueueRunResponse
{
    [DataMember(Order = 1)]
    public TaskQueueItem QueueItem { get; set; } = new TaskQueueItem();

    [DataMember(Order = 2)]
    public TaskRun Run { get; set; } = new TaskRun();

    [DataMember(Order = 3)]
    public TaskSummaryResponse Summary { get; set; } = new TaskSummaryResponse();

    [DataMember(Order = 4)]
    public ConnectorCallbackPreviewResponse CallbackPreview { get; set; } = new ConnectorCallbackPreviewResponse();
}

[DataContract]
public sealed class ConnectorCallbackPreviewRequest
{
    [DataMember(Order = 1)]
    public string RunId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string QueueItemId { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string ResultStatus { get; set; } = string.Empty;
}

[DataContract]
public sealed class ConnectorCallbackPayload
{
    [DataMember(Order = 1)]
    public string ExternalSystem { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string ExternalTaskRef { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string ProjectRef { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string RunId { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public string QueueItemId { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public string Status { get; set; } = string.Empty;

    [DataMember(Order = 7)]
    public string Summary { get; set; } = string.Empty;

    [DataMember(Order = 8)]
    public string NextAction { get; set; } = string.Empty;

    [DataMember(Order = 9)]
    public List<string> Artifacts { get; set; } = new List<string>();

    [DataMember(Order = 10)]
    public List<string> ResidualRisks { get; set; } = new List<string>();
}

[DataContract]
public sealed class ConnectorCallbackPreviewResponse
{
    [DataMember(Order = 1)]
    public string System { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string Reference { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string Mode { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string SuggestedStatus { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public string Summary { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public ConnectorCallbackPayload Payload { get; set; } = new ConnectorCallbackPayload();

    [DataMember(Order = 7)]
    public string PayloadJson { get; set; } = string.Empty;
}
