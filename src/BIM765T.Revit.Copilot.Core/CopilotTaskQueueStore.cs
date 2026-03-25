using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BIM765T.Revit.Contracts.Platform;
using BIM765T.Revit.Contracts.Serialization;

namespace BIM765T.Revit.Copilot.Core;

public sealed class CopilotTaskQueueStore
{
    private readonly object _gate = new object();
    private readonly CopilotStatePaths _paths;

    public CopilotTaskQueueStore(CopilotStatePaths paths)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _paths.EnsureCreated();
    }

    public TaskQueueItem Save(TaskQueueItem item)
    {
        if (item == null)
        {
            throw new ArgumentNullException(nameof(item));
        }

        item = Normalize(item);
        if (string.IsNullOrWhiteSpace(item.QueueItemId))
        {
            item.QueueItemId = Guid.NewGuid().ToString("N");
        }

        if (item.CreatedUtc == default)
        {
            item.CreatedUtc = DateTime.UtcNow;
        }

        item.UpdatedUtc = DateTime.UtcNow;
        var path = _paths.GetTaskQueueItemPath(item.QueueItemId);
        var payload = JsonUtil.Serialize(item);
        lock (_gate)
        {
            File.WriteAllText(path, payload);
        }

        return item;
    }

    public TaskQueueItem? TryGet(string queueItemId)
    {
        if (string.IsNullOrWhiteSpace(queueItemId))
        {
            return null;
        }

        var path = _paths.GetTaskQueueItemPath(queueItemId);
        if (!File.Exists(path))
        {
            return null;
        }

        lock (_gate)
        {
            return Normalize(JsonUtil.Deserialize<TaskQueueItem>(File.ReadAllText(path)));
        }
    }

    public List<TaskQueueItem> List(TaskQueueListRequest? request = null)
    {
        request ??= new TaskQueueListRequest();
        var items = new List<TaskQueueItem>();
        foreach (var file in Directory.GetFiles(_paths.TaskQueuePath, "*.json"))
        {
            try
            {
                string payload;
                lock (_gate)
                {
                    payload = File.ReadAllText(file);
                }

                items.Add(Normalize(JsonUtil.Deserialize<TaskQueueItem>(payload)));
            }
            catch
            {
                // keep queue resilient to partial/corrupt files
            }
        }

        if (!string.IsNullOrWhiteSpace(request.QueueName))
        {
            items = items.Where(x => string.Equals(x.QueueName, request.QueueName, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            items = items.Where(x => string.Equals(x.Status, request.Status, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        else if (!request.IncludeCompleted)
        {
            items = items.Where(x =>
                !string.Equals(x.Status, "completed", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(x.Status, "failed", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(x.Status, "cancelled", StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (!string.IsNullOrWhiteSpace(request.RunId))
        {
            items = items.Where(x => string.Equals(x.RunId, request.RunId, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (!string.IsNullOrWhiteSpace(request.ConnectorSystem))
        {
            items = items.Where(x => string.Equals(x.ConnectorSystem, request.ConnectorSystem, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        return items
            .OrderBy(x => x.ScheduledUtc ?? x.CreatedUtc)
            .ThenBy(x => x.CreatedUtc)
            .Take(Math.Max(1, request.MaxResults))
            .ToList();
    }

    public int Count(string status = "")
    {
        return string.IsNullOrWhiteSpace(status)
            ? List(new TaskQueueListRequest { MaxResults = int.MaxValue, IncludeCompleted = true }).Count
            : List(new TaskQueueListRequest { MaxResults = int.MaxValue, IncludeCompleted = true, Status = status }).Count;
    }

    private static TaskQueueItem Normalize(TaskQueueItem? item)
    {
        item ??= new TaskQueueItem();
        item.QueueItemId ??= string.Empty;
        item.RunId ??= string.Empty;
        item.TaskKind ??= string.Empty;
        item.TaskName ??= string.Empty;
        item.DocumentKey ??= string.Empty;
        item.QueueName = string.IsNullOrWhiteSpace(item.QueueName) ? "approved" : item.QueueName;
        item.Status = string.IsNullOrWhiteSpace(item.Status) ? "pending" : item.Status;
        item.ConnectorSystem ??= string.Empty;
        item.ExternalTaskRef ??= string.Empty;
        item.CallbackMode = string.IsNullOrWhiteSpace(item.CallbackMode) ? "panel_only" : item.CallbackMode;
        item.ApprovalToken ??= string.Empty;
        item.PreviewRunId ??= string.Empty;
        item.EnqueuedByCaller ??= string.Empty;
        item.LeaseOwner ??= string.Empty;
        item.Note ??= string.Empty;
        item.LastStatusMessage ??= string.Empty;
        item.ArtifactKeys ??= new List<string>();
        return item;
    }
}
