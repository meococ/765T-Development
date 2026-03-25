using System;
using System.Collections.Generic;
using BIM765T.Revit.Contracts.Platform;
using BIM765T.Revit.Contracts.Serialization;

namespace BIM765T.Revit.Copilot.Core;

public sealed class ConnectorCallbackPreviewService
{
    public ConnectorCallbackPreviewResponse Build(TaskRun? run, TaskQueueItem? queueItem = null, string requestedStatus = "")
    {
        run ??= new TaskRun();
        var report = run.RunReport ?? new RunReport();
        var connector = run.ConnectorTask ?? new ConnectorTaskEnvelope();
        var callback = run.TaskSpec?.CallbackTarget ?? new TaskCallbackTarget();

        var system = !string.IsNullOrWhiteSpace(callback.System) ? callback.System : connector.ExternalSystem;
        var reference = !string.IsNullOrWhiteSpace(callback.Reference) ? callback.Reference : connector.ExternalTaskRef;
        var mode = !string.IsNullOrWhiteSpace(callback.Mode) ? callback.Mode : connector.CallbackMode;
        var status = ResolveStatus(run, queueItem, requestedStatus, connector.StatusMapping);
        var summary = !string.IsNullOrWhiteSpace(report.TaskSummary)
            ? report.TaskSummary
            : $"{run.TaskKind}:{run.TaskName}";

        var payload = new ConnectorCallbackPayload
        {
            ExternalSystem = system ?? string.Empty,
            ExternalTaskRef = reference ?? string.Empty,
            ProjectRef = connector.ProjectRef ?? string.Empty,
            RunId = run.RunId ?? string.Empty,
            QueueItemId = queueItem?.QueueItemId ?? run.LastQueueItemId ?? string.Empty,
            Status = status,
            Summary = summary,
            NextAction = report.NextRecommendedAction ?? string.Empty,
            Artifacts = run.ArtifactKeys != null ? new List<string>(run.ArtifactKeys) : new List<string>(),
            ResidualRisks = report.ResidualRisks != null ? new List<string>(report.ResidualRisks) : new List<string>()
        };

        return new ConnectorCallbackPreviewResponse
        {
            System = system ?? string.Empty,
            Reference = reference ?? string.Empty,
            Mode = string.IsNullOrWhiteSpace(mode) ? "panel_only" : mode,
            SuggestedStatus = status,
            Summary = summary,
            Payload = payload,
            PayloadJson = JsonUtil.Serialize(payload)
        };
    }

    private static string ResolveStatus(TaskRun run, TaskQueueItem? queueItem, string requestedStatus, IDictionary<string, string>? statusMapping)
    {
        var desired = !string.IsNullOrWhiteSpace(requestedStatus)
            ? requestedStatus.Trim()
            : !string.IsNullOrWhiteSpace(queueItem?.Status)
                ? NormalizeRunStatus(queueItem?.Status)
                : NormalizeRunStatus(run.Status);

        if (statusMapping != null && statusMapping.TryGetValue(desired, out var mapped) && !string.IsNullOrWhiteSpace(mapped))
        {
            return mapped;
        }

        return desired;
    }

    private static string NormalizeRunStatus(string? status)
    {
        if (string.Equals(status, "verified", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase))
        {
            return "done";
        }

        if (string.Equals(status, "blocked", StringComparison.OrdinalIgnoreCase))
        {
            return "blocked";
        }

        if (string.Equals(status, "approved", StringComparison.OrdinalIgnoreCase))
        {
            return "approved";
        }

        if (string.Equals(status, "preview_ready", StringComparison.OrdinalIgnoreCase))
        {
            return "ready_for_approval";
        }

        return string.IsNullOrWhiteSpace(status) ? "planned" : (status?.Trim().ToLowerInvariant() ?? "planned");
    }
}
