using System;
using System.Collections.Generic;
using System.Linq;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Copilot.Core;

public sealed class TaskQueueCoordinator
{
    private readonly CopilotTaskQueueStore _store;

    public TaskQueueCoordinator(CopilotTaskQueueStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public TaskQueueItem? TryGet(string queueItemId)
    {
        return _store.TryGet(queueItemId);
    }

    public List<TaskQueueItem> List(TaskQueueListRequest? request = null)
    {
        return _store.List(request);
    }

    public int Count(string status = "")
    {
        return _store.Count(status);
    }

    public TaskQueueItem Enqueue(TaskRun run, TaskQueueEnqueueRequest? request, string caller)
    {
        run ??= new TaskRun();
        request ??= new TaskQueueEnqueueRequest();
        EnsureRunCanBeQueued(run);

        var existing = _store.List(new TaskQueueListRequest
        {
            RunId = run.RunId,
            IncludeCompleted = false,
            MaxResults = 20
        }).FirstOrDefault();
        if (existing != null)
        {
            throw new InvalidOperationException(StatusCodes.TaskQueueAlreadyQueued);
        }

        return _store.Save(new TaskQueueItem
        {
            RunId = run.RunId,
            TaskKind = run.TaskKind,
            TaskName = run.TaskName,
            DocumentKey = run.DocumentKey,
            QueueName = string.IsNullOrWhiteSpace(request.QueueName) ? "approved" : request.QueueName.Trim(),
            Status = "pending",
            ConnectorSystem = run.ConnectorTask?.ExternalSystem ?? string.Empty,
            ExternalTaskRef = run.ConnectorTask?.ExternalTaskRef ?? string.Empty,
            CallbackMode = run.TaskSpec?.CallbackTarget?.Mode ?? run.ConnectorTask?.CallbackMode ?? "panel_only",
            ApprovalToken = run.ApprovalToken ?? string.Empty,
            PreviewRunId = run.PreviewRunId ?? string.Empty,
            EnqueuedByCaller = caller ?? string.Empty,
            Note = request.Note ?? string.Empty,
            ScheduledUtc = request.ScheduledUtc,
            ArtifactKeys = run.ArtifactKeys?.ToList() ?? new List<string>()
        });
    }

    public TaskQueueItem ClaimNext(TaskQueueClaimRequest? request)
    {
        request ??= new TaskQueueClaimRequest();
        if (string.IsNullOrWhiteSpace(request.LeaseOwner))
        {
            throw new InvalidOperationException(StatusCodes.TaskQueueLeaseOwnerRequired);
        }

        var now = DateTime.UtcNow;
        var next = _store.List(new TaskQueueListRequest
        {
            QueueName = request.QueueName,
            Status = "pending",
            IncludeCompleted = false,
            MaxResults = 500
        })
        .FirstOrDefault(item => request.IncludeScheduledFuture || !item.ScheduledUtc.HasValue || item.ScheduledUtc.Value <= now);

        if (next == null)
        {
            throw new InvalidOperationException(StatusCodes.TaskQueueNoPendingItems);
        }

        next.Status = "leased";
        next.LeaseOwner = request.LeaseOwner.Trim();
        next.LeasedUtc = now;
        next.LastStatusMessage = "Queue item leased for execution.";
        return _store.Save(next);
    }

    public TaskQueueItem Claim(string queueItemId, string leaseOwner)
    {
        if (string.IsNullOrWhiteSpace(leaseOwner))
        {
            throw new InvalidOperationException(StatusCodes.TaskQueueLeaseOwnerRequired);
        }

        var item = _store.TryGet(queueItemId);
        if (item == null)
        {
            throw new InvalidOperationException(StatusCodes.TaskQueueNotFound);
        }

        if (IsTerminal(item.Status))
        {
            throw new InvalidOperationException(StatusCodes.TaskQueueAlreadyCompleted);
        }

        if (string.Equals(item.Status, "leased", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.Equals(item.LeaseOwner, leaseOwner, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(StatusCodes.TaskQueueLeaseMismatch);
            }

            return item;
        }

        item.Status = "leased";
        item.LeaseOwner = leaseOwner.Trim();
        item.LeasedUtc = DateTime.UtcNow;
        item.LastStatusMessage = "Queue item leased for execution.";
        return _store.Save(item);
    }

    public TaskQueueItem Complete(TaskQueueCompleteRequest? request, TaskRun? run = null)
    {
        request ??= new TaskQueueCompleteRequest();
        var item = _store.TryGet(request.QueueItemId);
        if (item == null)
        {
            throw new InvalidOperationException(StatusCodes.TaskQueueNotFound);
        }

        if (IsTerminal(item.Status))
        {
            throw new InvalidOperationException(StatusCodes.TaskQueueAlreadyCompleted);
        }

        item.Status = NormalizeFinalStatus(request.Status);
        item.LastStatusMessage = !string.IsNullOrWhiteSpace(request.Message)
            ? request.Message.Trim()
            : BuildCompletionMessage(run, item.Status);
        item.CompletedUtc = DateTime.UtcNow;
        item.ArtifactKeys = request.ArtifactKeys != null && request.ArtifactKeys.Count > 0
            ? request.ArtifactKeys.ToList()
            : run?.ArtifactKeys?.ToList() ?? item.ArtifactKeys ?? new List<string>();
        return _store.Save(item);
    }

    public TaskQueueItem? Reconcile(TaskRun? run)
    {
        if (run == null || string.IsNullOrWhiteSpace(run.RunId))
        {
            return null;
        }

        var active = !string.IsNullOrWhiteSpace(run.LastQueueItemId)
            ? _store.TryGet(run.LastQueueItemId)
            : _store.List(new TaskQueueListRequest
            {
                RunId = run.RunId,
                IncludeCompleted = false,
                MaxResults = 20
            }).FirstOrDefault();

        if (active == null || IsTerminal(active.Status))
        {
            return active;
        }

        if (string.Equals(run.Status, "verified", StringComparison.OrdinalIgnoreCase)
            || string.Equals(run.Status, "completed", StringComparison.OrdinalIgnoreCase))
        {
            return Complete(new TaskQueueCompleteRequest
            {
                QueueItemId = active.QueueItemId,
                Status = "completed",
                Message = BuildCompletionMessage(run, "completed"),
                ArtifactKeys = run.ArtifactKeys?.ToList() ?? new List<string>()
            }, run);
        }

        if (string.Equals(run.Status, "blocked", StringComparison.OrdinalIgnoreCase))
        {
            return Complete(new TaskQueueCompleteRequest
            {
                QueueItemId = active.QueueItemId,
                Status = "failed",
                Message = BuildCompletionMessage(run, "failed"),
                ArtifactKeys = run.ArtifactKeys?.ToList() ?? new List<string>()
            }, run);
        }

        return active;
    }

    private static void EnsureRunCanBeQueued(TaskRun run)
    {
        var queueEligible = run.QueueEligible || run.TaskSpec?.ApprovalPolicy?.AllowQueuedExecution == true;
        if (!queueEligible)
        {
            throw new InvalidOperationException(StatusCodes.TaskQueueBlocked);
        }

        if (string.Equals(run.Status, "completed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(run.Status, "verified", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(StatusCodes.TaskAlreadyCompleted);
        }

        var requiresApproval = run.TaskSpec?.ApprovalPolicy?.RequiresOperatorApproval != false;
        if (requiresApproval
            && (!string.Equals(run.Status, "approved", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(run.ApprovalToken)))
        {
            throw new InvalidOperationException(StatusCodes.TaskQueueApprovalRequired);
        }
    }

    private static bool IsTerminal(string? status)
    {
        return string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "cancelled", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeFinalStatus(string? status)
    {
        if (string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "cancelled", StringComparison.OrdinalIgnoreCase))
        {
            return status?.Trim() ?? "completed";
        }

        return "completed";
    }

    private static string BuildCompletionMessage(TaskRun? run, string finalStatus)
    {
        if (run == null)
        {
            return "Queue item marked " + finalStatus + ".";
        }

        if (string.Equals(finalStatus, "failed", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(run.LastErrorCode))
        {
            return $"{run.LastErrorCode}: {run.LastErrorMessage}".Trim();
        }

        var taskSummary = run.RunReport?.TaskSummary;
        if (!string.IsNullOrWhiteSpace(taskSummary))
        {
            return taskSummary ?? string.Empty;
        }

        return $"{run.TaskKind}:{run.TaskName} -> {finalStatus}";
    }
}
