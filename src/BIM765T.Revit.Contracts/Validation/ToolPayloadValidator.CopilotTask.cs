using System;
using System.Collections.Generic;
using System.Linq;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Contracts.Validation;

public static partial class ToolPayloadValidator
{
    private static void ValidateTaskPlan(TaskPlanRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(request.TaskKind))
        {
            diagnostics.Add(DiagnosticRecord.Create("TASK_KIND_REQUIRED", DiagnosticSeverity.Error, "TaskKind khong duoc rong."));
        }

        if (string.IsNullOrWhiteSpace(request.TaskName))
        {
            diagnostics.Add(DiagnosticRecord.Create("TASK_NAME_REQUIRED", DiagnosticSeverity.Error, "TaskName khong duoc rong."));
        }

        if (!string.IsNullOrWhiteSpace(request.PreferredCapabilityPack)
            && !new[]
            {
                WorkerCapabilityPacks.CoreWorker,
                WorkerCapabilityPacks.MemoryAndSoul,
                WorkerCapabilityPacks.Connector,
                WorkerCapabilityPacks.AutomationLab
            }.Contains(request.PreferredCapabilityPack, StringComparer.OrdinalIgnoreCase))
        {
            diagnostics.Add(DiagnosticRecord.Create("TASK_CAPABILITY_PACK_INVALID", DiagnosticSeverity.Error, "PreferredCapabilityPack khong hop le."));
        }

        if (request.TaskSpec?.ApprovalPolicy != null && request.TaskSpec.ApprovalPolicy.MaxBatchSize <= 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("TASK_APPROVAL_BATCH_INVALID", DiagnosticSeverity.Error, "TaskSpec.ApprovalPolicy.MaxBatchSize phai > 0."));
        }
    }

    private static void ValidateTaskPreview(TaskPreviewRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(request.RunId))
        {
            diagnostics.Add(DiagnosticRecord.Create("TASK_RUN_REQUIRED", DiagnosticSeverity.Error, "RunId khong duoc rong."));
        }
    }

    private static void ValidateTaskApproveStep(TaskApproveStepRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(request.RunId))
        {
            diagnostics.Add(DiagnosticRecord.Create("TASK_RUN_REQUIRED", DiagnosticSeverity.Error, "RunId khong duoc rong."));
        }
    }

    private static void ValidateTaskExecuteStep(TaskExecuteStepRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(request.RunId))
        {
            diagnostics.Add(DiagnosticRecord.Create("TASK_RUN_REQUIRED", DiagnosticSeverity.Error, "RunId khong duoc rong."));
        }
    }

    private static void ValidateTaskResume(TaskResumeRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(request.RunId))
        {
            diagnostics.Add(DiagnosticRecord.Create("TASK_RUN_REQUIRED", DiagnosticSeverity.Error, "RunId khong duoc rong."));
        }

        if (request.MaxResidualIssues <= 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("TASK_MAX_RESIDUAL_INVALID", DiagnosticSeverity.Error, "MaxResidualIssues phai > 0."));
        }
    }

    private static void ValidateTaskVerify(TaskVerifyRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(request.RunId))
        {
            diagnostics.Add(DiagnosticRecord.Create("TASK_RUN_REQUIRED", DiagnosticSeverity.Error, "RunId khong duoc rong."));
        }

        if (request.MaxResidualIssues <= 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("TASK_MAX_RESIDUAL_INVALID", DiagnosticSeverity.Error, "MaxResidualIssues phai > 0."));
        }
    }

    private static void ValidateTaskGetRun(TaskGetRunRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(request.RunId))
        {
            diagnostics.Add(DiagnosticRecord.Create("TASK_RUN_REQUIRED", DiagnosticSeverity.Error, "RunId khong duoc rong."));
        }
    }

    private static void ValidateTaskListRuns(TaskListRunsRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (request.MaxResults <= 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("TASK_MAX_RESULTS_INVALID", DiagnosticSeverity.Error, "MaxResults phai > 0."));
        }
    }

    private static void ValidateTaskSummarize(TaskSummarizeRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(request.RunId))
        {
            diagnostics.Add(DiagnosticRecord.Create("TASK_RUN_REQUIRED", DiagnosticSeverity.Error, "RunId khong duoc rong."));
        }
    }

    private static void ValidateTaskMetrics(TaskMetricsRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (request.MaxResults <= 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("TASK_MAX_RESULTS_INVALID", DiagnosticSeverity.Error, "MaxResults phai > 0."));
        }
    }

    private static void ValidateTaskResiduals(TaskResidualsRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(request.RunId))
        {
            diagnostics.Add(DiagnosticRecord.Create("TASK_RUN_REQUIRED", DiagnosticSeverity.Error, "RunId khong duoc rong."));
        }
    }

    private static void ValidateTaskPromoteMemory(TaskPromoteMemoryRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(request.RunId))
        {
            diagnostics.Add(DiagnosticRecord.Create("TASK_RUN_REQUIRED", DiagnosticSeverity.Error, "RunId khong duoc rong."));
        }

        if (string.IsNullOrWhiteSpace(request.PromotionKind))
        {
            diagnostics.Add(DiagnosticRecord.Create("TASK_PROMOTION_KIND_REQUIRED", DiagnosticSeverity.Error, "PromotionKind khong duoc rong."));
        }
    }

    private static void ValidateTaskQueueEnqueue(TaskQueueEnqueueRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(request.RunId))
        {
            diagnostics.Add(DiagnosticRecord.Create("TASK_RUN_REQUIRED", DiagnosticSeverity.Error, "RunId khong duoc rong."));
        }
    }

    private static void ValidateTaskQueueClaim(TaskQueueClaimRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(request.LeaseOwner))
        {
            diagnostics.Add(DiagnosticRecord.Create("TASK_QUEUE_LEASE_OWNER_REQUIRED", DiagnosticSeverity.Error, "LeaseOwner khong duoc rong."));
        }
    }

    private static void ValidateTaskQueueComplete(TaskQueueCompleteRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(request.QueueItemId))
        {
            diagnostics.Add(DiagnosticRecord.Create("TASK_QUEUE_ITEM_REQUIRED", DiagnosticSeverity.Error, "QueueItemId khong duoc rong."));
        }
    }

    private static void ValidateTaskQueueRun(TaskQueueRunRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(request.QueueItemId))
        {
            diagnostics.Add(DiagnosticRecord.Create("TASK_QUEUE_ITEM_REQUIRED", DiagnosticSeverity.Error, "QueueItemId khong duoc rong."));
        }

        if (string.IsNullOrWhiteSpace(request.LeaseOwner))
        {
            diagnostics.Add(DiagnosticRecord.Create("TASK_QUEUE_LEASE_OWNER_REQUIRED", DiagnosticSeverity.Error, "LeaseOwner khong duoc rong."));
        }

        if (request.MaxResidualIssues <= 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("TASK_MAX_RESIDUAL_INVALID", DiagnosticSeverity.Error, "MaxResidualIssues phai > 0."));
        }
    }

    private static void ValidateTaskQueueList(TaskQueueListRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (request.MaxResults <= 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("TASK_QUEUE_MAX_RESULTS_INVALID", DiagnosticSeverity.Error, "MaxResults phai > 0."));
        }
    }

    private static void ValidateConnectorCallbackPreview(ConnectorCallbackPreviewRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(request.RunId))
        {
            diagnostics.Add(DiagnosticRecord.Create("TASK_RUN_REQUIRED", DiagnosticSeverity.Error, "RunId khong duoc rong."));
        }
    }

    private static void ValidateHotState(HotStateRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (request.MaxRecentOperations < 0 || request.MaxRecentEvents < 0 || request.MaxPendingTasks < 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("HOT_STATE_LIMIT_INVALID", DiagnosticSeverity.Error, "Hot state limits khong duoc am."));
        }
    }

    private static void ValidateContextDeltaSummary(ContextDeltaSummaryRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (request.MaxRecentOperations <= 0 || request.MaxRecentEvents <= 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("CONTEXT_DELTA_LIMIT_INVALID", DiagnosticSeverity.Error, "MaxRecentOperations/MaxRecentEvents phai > 0."));
        }

        if (request.MaxRecommendations <= 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("CONTEXT_DELTA_RECOMMENDATIONS_INVALID", DiagnosticSeverity.Error, "MaxRecommendations phai > 0."));
        }
    }

    private static void ValidateContextResolveBundle(ContextResolveBundleRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (request.MaxAnchors <= 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("CONTEXT_MAX_ANCHORS_INVALID", DiagnosticSeverity.Error, "MaxAnchors phai > 0."));
        }
    }

    private static void ValidateContextSearchAnchors(ContextSearchAnchorsRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (request.MaxResults <= 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("CONTEXT_MAX_RESULTS_INVALID", DiagnosticSeverity.Error, "MaxResults phai > 0."));
        }
    }

    private static void ValidateArtifactSummarize(ArtifactSummarizeRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(request.ArtifactPath))
        {
            diagnostics.Add(DiagnosticRecord.Create("ARTIFACT_PATH_REQUIRED", DiagnosticSeverity.Error, "ArtifactPath khong duoc rong."));
        }

        if (request.MaxChars <= 0 || request.MaxLines <= 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("ARTIFACT_SUMMARY_LIMIT_INVALID", DiagnosticSeverity.Error, "MaxChars/MaxLines phai > 0."));
        }
    }

    private static void ValidateMemoryFindSimilarRuns(MemoryFindSimilarRunsRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (request.MaxResults <= 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("MEMORY_MAX_RESULTS_INVALID", DiagnosticSeverity.Error, "MaxResults phai > 0."));
        }
    }

    private static void ValidateToolCapabilityLookup(ToolCapabilityLookupRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (request.MaxResults <= 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("TOOL_LOOKUP_MAX_RESULTS_INVALID", DiagnosticSeverity.Error, "MaxResults phai > 0."));
        }
    }

    private static void ValidateToolGuidance(ToolGuidanceRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (request.MaxResults <= 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("TOOL_GUIDANCE_MAX_RESULTS_INVALID", DiagnosticSeverity.Error, "MaxResults phai > 0."));
        }

        if (string.IsNullOrWhiteSpace(request.Query) && (request.ToolNames == null || request.ToolNames.Count == 0))
        {
            diagnostics.Add(DiagnosticRecord.Create("TOOL_GUIDANCE_QUERY_REQUIRED", DiagnosticSeverity.Error, "Query hoac ToolNames phai co it nhat 1 gia tri."));
        }
    }
}
