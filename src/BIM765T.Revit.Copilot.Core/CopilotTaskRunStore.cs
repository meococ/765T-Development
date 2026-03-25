using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BIM765T.Revit.Contracts.Platform;
using BIM765T.Revit.Contracts.Serialization;

namespace BIM765T.Revit.Copilot.Core;

public sealed class CopilotTaskRunStore
{
    private readonly object _gate = new object();
    private readonly CopilotStatePaths _paths;

    public CopilotTaskRunStore(CopilotStatePaths paths)
    {
        _paths = paths;
        _paths.EnsureCreated();
    }

    public string RootPath => _paths.RootPath;

    public TaskRun Save(TaskRun run)
    {
        if (run == null)
        {
            throw new ArgumentNullException(nameof(run));
        }

        run = NormalizeRun(run);

        if (string.IsNullOrWhiteSpace(run.RunId))
        {
            run.RunId = Guid.NewGuid().ToString("N");
        }

        if (run.CreatedUtc == default)
        {
            run.CreatedUtc = DateTime.UtcNow;
        }

        run.UpdatedUtc = DateTime.UtcNow;
        var path = _paths.GetRunPath(run.RunId);
        var payload = JsonUtil.Serialize(run);
        lock (_gate)
        {
            File.WriteAllText(path, payload);
        }

        return run;
    }

    public TaskRun? TryGet(string runId)
    {
        if (string.IsNullOrWhiteSpace(runId))
        {
            return null;
        }

        var path = _paths.GetRunPath(runId);
        if (!File.Exists(path))
        {
            return null;
        }

        lock (_gate)
        {
            return NormalizeRun(JsonUtil.Deserialize<TaskRun>(File.ReadAllText(path)));
        }
    }

    public List<TaskRun> List(TaskListRunsRequest? request = null)
    {
        request ??= new TaskListRunsRequest();
        var runs = new List<TaskRun>();
        foreach (var file in Directory.GetFiles(_paths.TaskRunsPath, "*.json"))
        {
            try
            {
                string payload;
                lock (_gate)
                {
                    payload = File.ReadAllText(file);
                }

                runs.Add(NormalizeRun(JsonUtil.Deserialize<TaskRun>(payload)));
            }
            catch
            {
                // ignore corrupted files and keep the store resilient
            }
        }

        if (!string.IsNullOrWhiteSpace(request.TaskKind))
        {
            runs = runs.Where(x => string.Equals(x.TaskKind, request.TaskKind, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (!string.IsNullOrWhiteSpace(request.TaskName))
        {
            runs = runs.Where(x => string.Equals(x.TaskName, request.TaskName, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (!string.IsNullOrWhiteSpace(request.DocumentKey))
        {
            runs = runs.Where(x => string.Equals(x.DocumentKey, request.DocumentKey, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            runs = runs.Where(x => string.Equals(x.Status, request.Status, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        return runs
            .OrderByDescending(x => x.UpdatedUtc)
            .Take(Math.Max(1, request.MaxResults))
            .ToList();
    }

    public int CountRuns()
    {
        return Directory.Exists(_paths.TaskRunsPath)
            ? Directory.GetFiles(_paths.TaskRunsPath, "*.json").Length
            : 0;
    }

    public TaskMemoryPromotionRecord SavePromotion(TaskMemoryPromotionRecord record)
    {
        if (record == null)
        {
            throw new ArgumentNullException(nameof(record));
        }

        record.MemoryRecord ??= new MemoryRecord();

        if (string.IsNullOrWhiteSpace(record.PromotionId))
        {
            record.PromotionId = Guid.NewGuid().ToString("N");
        }

        if (record.CreatedUtc == default)
        {
            record.CreatedUtc = DateTime.UtcNow;
        }

        var path = _paths.GetPromotionPath(record.PromotionId);
        lock (_gate)
        {
            File.WriteAllText(path, JsonUtil.Serialize(record));
        }

        return record;
    }

    public List<TaskMemoryPromotionRecord> ListPromotions()
    {
        var records = new List<TaskMemoryPromotionRecord>();
        foreach (var file in Directory.GetFiles(_paths.MemoryPromotionsPath, "*.json"))
        {
            try
            {
                string payload;
                lock (_gate)
                {
                    payload = File.ReadAllText(file);
                }

                var record = JsonUtil.Deserialize<TaskMemoryPromotionRecord>(payload);
                if (record != null)
                {
                    record.MemoryRecord ??= new MemoryRecord();
                    records.Add(record);
                }
            }
            catch
            {
                // ignore corrupted files and keep the store resilient
            }
        }

        return records
            .OrderByDescending(x => x.CreatedUtc)
            .ToList();
    }

    public int CountPromotions()
    {
        return Directory.Exists(_paths.MemoryPromotionsPath)
            ? Directory.GetFiles(_paths.MemoryPromotionsPath, "*.json").Length
            : 0;
    }

    private static TaskRun NormalizeRun(TaskRun? run)
    {
        run ??= new TaskRun();
        run.RecommendedActionIds ??= new List<string>();
        run.SelectedActionIds ??= new List<string>();
        run.Steps ??= new List<TaskStepState>();
        run.Diagnostics ??= new List<Contracts.Common.DiagnosticRecord>();
        run.ChangedIds ??= new List<int>();
        run.ArtifactKeys ??= new List<string>();
        run.Tags ??= new List<string>();
        run.Checkpoints ??= new List<TaskCheckpointRecord>();
        run.RecoveryBranches ??= new List<TaskRecoveryBranch>();
        run.IntentSummary ??= string.Empty;
        run.PlanSummary ??= string.Empty;
        run.InputJson ??= "{}";
        run.DocumentKey ??= string.Empty;
        run.TaskKind ??= string.Empty;
        run.TaskName ??= string.Empty;
        run.Status ??= "planned";
        run.ExpectedContextJson ??= string.Empty;
        run.ApprovalToken ??= string.Empty;
        run.PreviewRunId ??= string.Empty;
        run.PlannedByCaller ??= string.Empty;
        run.PlannedBySessionId ??= string.Empty;
        run.ApprovedByCaller ??= string.Empty;
        run.ApprovedBySessionId ??= string.Empty;
        run.ApprovalNote ??= string.Empty;
        run.VerificationStatus ??= string.Empty;
        run.ResidualSummary ??= string.Empty;
        run.LastErrorCode ??= string.Empty;
        run.LastErrorMessage ??= string.Empty;
        run.TaskSpec ??= new TaskSpec();
        run.WorkerProfile ??= new WorkerProfile();
        run.RunReport ??= new RunReport();
        run.CapabilityPack ??= WorkerCapabilityPacks.CoreWorker;
        run.PrimarySkillGroup ??= WorkerSkillGroups.Orchestration;
        run.ConnectorTask ??= new ConnectorTaskEnvelope();
        run.LastQueueItemId ??= string.Empty;
        return run;
    }
}
