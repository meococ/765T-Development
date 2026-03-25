using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BIM765T.Revit.Contracts.Platform;
using BIM765T.Revit.Contracts.Serialization;
using BIM765T.Revit.Copilot.Core;
using BIM765T.Revit.WorkerHost.Configuration;
using BIM765T.Revit.WorkerHost.Eventing;
using BIM765T.Revit.WorkerHost.Memory;
using StatusCodes = BIM765T.Revit.Contracts.Common.StatusCodes;

namespace BIM765T.Revit.WorkerHost.Migration;

internal sealed class LegacyStateMigrator
{
    private const string MigratorActor = "legacy-state-migrator";
    private readonly WorkerHostSettings _settings;
    private readonly SqliteMissionEventStore _store;
    private readonly IMemorySearchService _memorySearch;

    public LegacyStateMigrator(
        WorkerHostSettings settings,
        SqliteMissionEventStore store,
        IMemorySearchService memorySearch)
    {
        _settings = settings;
        _store = store;
        _memorySearch = memorySearch;
    }

    public async Task<LegacyMigrationReport> MigrateAsync(bool force, bool dryRun, CancellationToken cancellationToken)
    {
        var paths = new CopilotStatePaths(_settings.LegacyStateRootPath);
        var report = new LegacyMigrationReport
        {
            GeneratedUtc = DateTime.UtcNow.ToString("O"),
            SourceRootPath = paths.RootPath,
            DryRun = dryRun,
            ForceRequested = force
        };

        await ImportTaskRunsAsync(paths, report, cancellationToken).ConfigureAwait(false);
        await ImportPromotionsAsync(paths, report, cancellationToken).ConfigureAwait(false);
        await ImportEpisodesAsync(paths, report, cancellationToken).ConfigureAwait(false);
        await ImportQueueItemsAsync(paths, report, cancellationToken).ConfigureAwait(false);
        return report;
    }

    private async Task ImportTaskRunsAsync(CopilotStatePaths paths, LegacyMigrationReport report, CancellationToken cancellationToken)
    {
        foreach (var file in EnumerateJsonFiles(paths.TaskRunsPath))
        {
            report.TaskRuns.Scanned++;
            try
            {
                var run = JsonUtil.DeserializeRequired<TaskRun>(await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false));
                var sourceId = string.IsNullOrWhiteSpace(run.RunId) ? Path.GetFileNameWithoutExtension(file) : run.RunId;
                if (await _store.TryGetSnapshotAsync(sourceId, cancellationToken).ConfigureAwait(false) != null)
                {
                    report.TaskRuns.Skipped++;
                    TrackSample(report.TaskRuns, sourceId);
                    if (!report.DryRun)
                    {
                        await _store.TryRegisterMigrationAsync("task_run", sourceId, file, "already_present", cancellationToken).ConfigureAwait(false);
                    }
                    continue;
                }

                var (events, snapshot) = BuildTaskRunImport(run, sourceId);
                if (events.Count == 0)
                {
                    report.TaskRuns.Skipped++;
                    continue;
                }

                TrackSample(report.TaskRuns, sourceId);
                if (report.DryRun)
                {
                    report.TaskRuns.WouldImport++;
                    continue;
                }

                await _store.AppendBatchAsync(events, System.Text.Json.JsonSerializer.Serialize(snapshot), cancellationToken).ConfigureAwait(false);
                await _store.TryRegisterMigrationAsync("task_run", sourceId, file, "imported", cancellationToken).ConfigureAwait(false);
                report.TaskRuns.Imported++;
            }
            catch (Exception ex)
            {
                report.TaskRuns.Failed++;
                report.Errors.Add($"{file}: {ex.Message}");
            }
        }
    }

    private async Task ImportPromotionsAsync(CopilotStatePaths paths, LegacyMigrationReport report, CancellationToken cancellationToken)
    {
        foreach (var file in EnumerateJsonFiles(paths.MemoryPromotionsPath))
        {
            report.Promotions.Scanned++;
            try
            {
                var record = JsonUtil.DeserializeRequired<TaskMemoryPromotionRecord>(await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false));
                var id = string.IsNullOrWhiteSpace(record.PromotionId) ? Path.GetFileNameWithoutExtension(file) : record.PromotionId;
                var canImport = report.DryRun
                    ? true
                    : await _store.TryRegisterMigrationAsync("promotion", id, file, "imported", cancellationToken).ConfigureAwait(false);
                if (!canImport)
                {
                    report.Promotions.Skipped++;
                    TrackSample(report.Promotions, id);
                    continue;
                }

                TrackSample(report.Promotions, id);
                if (report.DryRun)
                {
                    report.Promotions.WouldImport++;
                    continue;
                }

                await _memorySearch.UpsertAsync(ToPromotionMemoryRecord(record, id, file), cancellationToken).ConfigureAwait(false);
                report.Promotions.Imported++;
            }
            catch (Exception ex)
            {
                report.Promotions.Failed++;
                report.Errors.Add($"{file}: {ex.Message}");
            }
        }
    }

    private async Task ImportEpisodesAsync(CopilotStatePaths paths, LegacyMigrationReport report, CancellationToken cancellationToken)
    {
        foreach (var file in EnumerateJsonFiles(paths.WorkerEpisodesPath))
        {
            report.Episodes.Scanned++;
            try
            {
                var record = JsonUtil.DeserializeRequired<EpisodicRecord>(await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false));
                var id = string.IsNullOrWhiteSpace(record.EpisodeId) ? Path.GetFileNameWithoutExtension(file) : record.EpisodeId;
                var canImport = report.DryRun
                    ? true
                    : await _store.TryRegisterMigrationAsync("episode", id, file, "imported", cancellationToken).ConfigureAwait(false);
                if (!canImport)
                {
                    report.Episodes.Skipped++;
                    TrackSample(report.Episodes, id);
                    continue;
                }

                TrackSample(report.Episodes, id);
                if (report.DryRun)
                {
                    report.Episodes.WouldImport++;
                    continue;
                }

                await _memorySearch.UpsertAsync(ToEpisodeMemoryRecord(record, id, file), cancellationToken).ConfigureAwait(false);
                report.Episodes.Imported++;
            }
            catch (Exception ex)
            {
                report.Episodes.Failed++;
                report.Errors.Add($"{file}: {ex.Message}");
            }
        }
    }

    private async Task ImportQueueItemsAsync(CopilotStatePaths paths, LegacyMigrationReport report, CancellationToken cancellationToken)
    {
        foreach (var file in EnumerateJsonFiles(paths.TaskQueuePath))
        {
            report.QueueItems.Scanned++;
            try
            {
                var item = JsonUtil.DeserializeRequired<TaskQueueItem>(await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false));
                var id = string.IsNullOrWhiteSpace(item.QueueItemId) ? Path.GetFileNameWithoutExtension(file) : item.QueueItemId;
                var canImport = report.DryRun
                    ? true
                    : await _store.TryRegisterMigrationAsync("queue_item", id, file, "imported", cancellationToken).ConfigureAwait(false);
                if (!canImport)
                {
                    report.QueueItems.Skipped++;
                    TrackSample(report.QueueItems, id);
                    continue;
                }

                TrackSample(report.QueueItems, id);
                if (report.DryRun)
                {
                    report.QueueItems.WouldImport++;
                    continue;
                }

                await _memorySearch.UpsertAsync(ToQueueMemoryRecord(item, id, file), cancellationToken).ConfigureAwait(false);
                report.QueueItems.Imported++;
            }
            catch (Exception ex)
            {
                report.QueueItems.Failed++;
                report.Errors.Add($"{file}: {ex.Message}");
            }
        }
    }

    private static (List<MissionEventRecord> Events, MissionSnapshot Snapshot) BuildTaskRunImport(TaskRun run, string sourceId)
    {
        var documentKey = run.DocumentKey ?? string.Empty;
        var correlationId = "legacy-task-run:" + sourceId;
        var createdUtc = NormalizeUtc(run.CreatedUtc, DateTime.UtcNow);
        var updatedUtc = NormalizeUtc(run.UpdatedUtc, run.CreatedUtc == default ? DateTime.UtcNow : run.UpdatedUtc);
        var state = ResolveMissionState(run);
        var snapshot = new MissionSnapshot
        {
            MissionId = sourceId,
            State = state,
            SessionId = FirstNonEmpty(run.PlannedBySessionId, run.ApprovedBySessionId, "legacy-session"),
            Intent = run.IntentSummary ?? string.Empty,
            RequestJson = string.IsNullOrWhiteSpace(run.InputJson) ? "{}" : run.InputJson,
            ResponseJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                legacy = true,
                run.RunId,
                run.Status,
                run.TaskKind,
                run.TaskName,
                run.PlanSummary,
                run.VerificationStatus,
                run.ResidualSummary,
                run.LastErrorCode,
                run.LastErrorMessage,
                run.ChangedIds,
                run.ArtifactKeys
            }),
            ResponseText = FirstNonEmpty(
                run.RunReport?.TaskSummary,
                run.ResidualSummary,
                run.LastErrorMessage,
                run.PlanSummary,
                run.IntentSummary),
            ApprovalToken = run.ApprovalToken ?? string.Empty,
            PreviewRunId = run.PreviewRunId ?? string.Empty,
            ExpectedContextJson = run.ExpectedContextJson ?? string.Empty,
            LastStatusCode = ResolveLastStatusCode(run, state),
            Terminal = IsTerminalState(state)
        };

        var events = new List<MissionEventRecord>
        {
            CreateEvent(sourceId, "TaskStarted", new
            {
                run.RunId,
                run.TaskKind,
                run.TaskName,
                input = snapshot.RequestJson,
                run.CapabilityPack,
                run.PrimarySkillGroup
            }, createdUtc, correlationId, documentKey)
        };

        if (!string.IsNullOrWhiteSpace(run.IntentSummary))
        {
            events.Add(CreateEvent(sourceId, "IntentClassified", new { run.IntentSummary, run.TaskKind, run.TaskName }, createdUtc, correlationId, documentKey));
        }

        if (!string.IsNullOrWhiteSpace(run.PlanSummary))
        {
            events.Add(CreateEvent(sourceId, "PlanBuilt", new { run.PlanSummary, run.RecommendedActionIds, run.SelectedActionIds }, createdUtc, correlationId, documentKey));
        }

        if (!string.IsNullOrWhiteSpace(run.PreviewRunId) || !string.IsNullOrWhiteSpace(run.ApprovalToken))
        {
            events.Add(CreateEvent(sourceId, "PreviewGenerated", new
            {
                run.PreviewRunId,
                run.ApprovalToken,
                run.ExpectedDelta,
                run.TaskSpec
            }, createdUtc, correlationId, documentKey));
            events.Add(CreateEvent(sourceId, "ApprovalRequested", new
            {
                run.ApprovalToken,
                run.PreviewRunId,
                run.ExpectedContextJson
            }, createdUtc, correlationId, documentKey));
        }

        if (!string.IsNullOrWhiteSpace(run.ApprovedByCaller) || !string.IsNullOrWhiteSpace(run.ApprovalNote))
        {
            events.Add(CreateEvent(sourceId, "UserApproved", new
            {
                run.ApprovedByCaller,
                run.ApprovedBySessionId,
                run.ApprovalNote
            }, updatedUtc, correlationId, documentKey));
        }

        if (ShouldEmitExecutionStarted(run, state))
        {
            events.Add(CreateEvent(sourceId, "ExecutionStarted", new
            {
                run.Status,
                run.DurationMs,
                run.UpdatedUtc
            }, updatedUtc, correlationId, documentKey));
        }

        if ((run.ChangedIds?.Count ?? 0) > 0 || (run.ArtifactKeys?.Count ?? 0) > 0)
        {
            events.Add(CreateEvent(sourceId, "RevitMutationApplied", new
            {
                run.ChangedIds,
                run.ArtifactKeys,
                run.ActualDelta
            }, updatedUtc, correlationId, documentKey));
        }

        if (!string.IsNullOrWhiteSpace(run.VerificationStatus))
        {
            var verificationEventType = LooksLikeVerificationFailure(run)
                ? "VerificationFailed"
                : "VerificationPassed";
            events.Add(CreateEvent(sourceId, verificationEventType, new
            {
                run.VerificationStatus,
                run.ResidualSummary,
                run.LastErrorCode,
                run.LastErrorMessage
            }, updatedUtc, correlationId, documentKey));
        }

        foreach (var checkpoint in run.Checkpoints ?? Enumerable.Empty<TaskCheckpointRecord>())
        {
            events.Add(CreateEvent(sourceId, "LegacyCheckpointImported", new
            {
                checkpoint.CheckpointId,
                checkpoint.StepId,
                checkpoint.Status,
                checkpoint.Summary,
                checkpoint.ReasonCode,
                checkpoint.ReasonMessage,
                checkpoint.NextAction,
                checkpoint.CanResume,
                checkpoint.ArtifactKeys,
                checkpoint.ChangedIds,
                checkpoint.ExpectedDelta,
                checkpoint.ActualDelta
            }, NormalizeUtc(checkpoint.CreatedUtc, run.UpdatedUtc == default ? DateTime.UtcNow : run.UpdatedUtc), correlationId, documentKey));
        }

        foreach (var branch in run.RecoveryBranches ?? Enumerable.Empty<TaskRecoveryBranch>())
        {
            events.Add(CreateEvent(sourceId, "LegacyRecoveryBranchImported", new
            {
                branch.BranchId,
                branch.Title,
                branch.Description,
                branch.NextAction,
                branch.ReasonCode,
                branch.RequiresApproval,
                branch.RequiresFreshPreview,
                branch.AutoResumable,
                branch.IsRecommended
            }, updatedUtc, correlationId, documentKey));
        }

        var finalEvent = CreateFinalEvent(run, sourceId, state, updatedUtc, correlationId, documentKey);
        if (finalEvent != null)
        {
            events.Add(finalEvent);
        }

        snapshot.Version = events.Count;
        return (events, snapshot);
    }

    private static PromotedMemoryRecord ToPromotionMemoryRecord(TaskMemoryPromotionRecord record, string sourceId, string file)
    {
        return new PromotedMemoryRecord
        {
            MemoryId = "promotion:" + sourceId,
            NamespaceId = string.Equals(record.MemoryRecord?.Kind, "lesson", StringComparison.OrdinalIgnoreCase)
                ? MemoryNamespaces.EvidenceLessons
                : MemoryNamespaces.PlaybooksPolicies,
            Kind = FirstNonEmpty(record.MemoryRecord?.Kind, record.PromotionKind, "promotion"),
            Title = FirstNonEmpty(record.MemoryRecord?.Summary, record.Summary, $"{record.TaskKind} {record.TaskName}".Trim()),
            Snippet = FirstNonEmpty(record.Notes, record.Summary, record.MemoryRecord?.Summary),
            SourceRef = file,
            DocumentKey = record.DocumentKey ?? string.Empty,
            EventType = "MemoryPromoted",
            RunId = record.RunId ?? string.Empty,
            Promoted = true,
            PayloadJson = JsonUtil.Serialize(record),
            CreatedUtc = NormalizeUtc(record.CreatedUtc, DateTime.UtcNow)
        };
    }

    private static PromotedMemoryRecord ToEpisodeMemoryRecord(EpisodicRecord record, string sourceId, string file)
    {
        return new PromotedMemoryRecord
        {
            MemoryId = "episode:" + sourceId,
            NamespaceId = MemoryNamespaces.ProjectRuntimeMemory,
            Kind = "episode",
            Title = FirstNonEmpty(record.MissionType, record.Outcome, "episode"),
            Snippet = FirstNonEmpty(record.Outcome, string.Join(" | ", record.KeyObservations ?? new List<string>())),
            SourceRef = file,
            DocumentKey = record.DocumentKey ?? string.Empty,
            EventType = "MemoryPromoted",
            RunId = record.RunId ?? string.Empty,
            Promoted = true,
            PayloadJson = JsonUtil.Serialize(record),
            CreatedUtc = NormalizeUtc(record.CreatedUtc, DateTime.UtcNow)
        };
    }

    private static PromotedMemoryRecord ToQueueMemoryRecord(TaskQueueItem item, string sourceId, string file)
    {
        return new PromotedMemoryRecord
        {
            MemoryId = "queue:" + sourceId,
            NamespaceId = MemoryNamespaces.ProjectRuntimeMemory,
            Kind = "legacy_queue_item",
            Title = FirstNonEmpty(item.TaskName, item.QueueName, "queue item"),
            Snippet = $"{item.Status} | {FirstNonEmpty(item.Note, item.LastStatusMessage, item.ExternalTaskRef)}".Trim(),
            SourceRef = file,
            DocumentKey = item.DocumentKey ?? string.Empty,
            EventType = "LegacyQueueImported",
            RunId = item.RunId ?? string.Empty,
            Promoted = true,
            PayloadJson = JsonUtil.Serialize(item),
            CreatedUtc = NormalizeUtc(item.UpdatedUtc, item.CreatedUtc == default ? DateTime.UtcNow : item.CreatedUtc)
        };
    }

    private static MissionEventRecord? CreateFinalEvent(
        TaskRun run,
        string sourceId,
        string state,
        string occurredUtc,
        string correlationId,
        string documentKey)
    {
        string eventType;
        if (string.Equals(state, WorkerMissionStates.Completed, StringComparison.OrdinalIgnoreCase))
        {
            eventType = "TaskCompleted";
        }
        else if (LooksLikeCanceled(run.Status))
        {
            eventType = "TaskCanceled";
        }
        else if (IsTerminalState(state))
        {
            eventType = "TaskBlocked";
        }
        else
        {
            return null;
        }

        return CreateEvent(sourceId, eventType, new
        {
            run.Status,
            run.LastErrorCode,
            run.LastErrorMessage,
            run.ResidualSummary
        }, occurredUtc, correlationId, documentKey, terminal: true);
    }

    private static MissionEventRecord CreateEvent(
        string streamId,
        string eventType,
        object payload,
        string occurredUtc,
        string correlationId,
        string documentKey,
        bool terminal = false)
    {
        return new MissionEventRecord
        {
            StreamId = streamId,
            EventType = eventType,
            PayloadJson = System.Text.Json.JsonSerializer.Serialize(payload),
            OccurredUtc = occurredUtc,
            CorrelationId = correlationId,
            CausationId = correlationId,
            ActorId = MigratorActor,
            DocumentKey = documentKey,
            Terminal = terminal
        };
    }

    private static string ResolveMissionState(TaskRun run)
    {
        var status = (run.Status ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(status))
        {
            return !string.IsNullOrWhiteSpace(run.ApprovalToken)
                ? WorkerMissionStates.AwaitingApproval
                : WorkerMissionStates.Planned;
        }

        if (LooksLikeCompleted(status))
        {
            return WorkerMissionStates.Completed;
        }

        if (LooksLikeCanceled(status))
        {
            return WorkerMissionStates.Blocked;
        }

        if (LooksLikeFailure(status))
        {
            return WorkerMissionStates.Failed;
        }

        if (status.IndexOf("block", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return WorkerMissionStates.Blocked;
        }

        if (status.IndexOf("verify", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return WorkerMissionStates.Verifying;
        }

        if (status.IndexOf("run", StringComparison.OrdinalIgnoreCase) >= 0
            || status.IndexOf("execute", StringComparison.OrdinalIgnoreCase) >= 0
            || status.IndexOf("apply", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return WorkerMissionStates.Running;
        }

        if (!string.IsNullOrWhiteSpace(run.ApprovalToken)
            && string.IsNullOrWhiteSpace(run.ApprovedByCaller))
        {
            return WorkerMissionStates.AwaitingApproval;
        }

        if (status.IndexOf("plan", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return WorkerMissionStates.Planned;
        }

        if (status.IndexOf("understand", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return WorkerMissionStates.Understanding;
        }

        return WorkerMissionStates.Running;
    }

    private static string ResolveLastStatusCode(TaskRun run, string state)
    {
        if (!string.IsNullOrWhiteSpace(run.LastErrorCode))
        {
            return run.LastErrorCode;
        }

        if (LooksLikeVerificationFailure(run))
        {
            return StatusCodes.FixLoopVerificationFailed;
        }

        return string.Equals(state, WorkerMissionStates.Completed, StringComparison.OrdinalIgnoreCase)
            ? StatusCodes.Ok
            : StatusCodes.ReadSucceeded;
    }

    private static bool LooksLikeVerificationFailure(TaskRun run)
    {
        return !string.IsNullOrWhiteSpace(run.LastErrorCode)
            || !string.IsNullOrWhiteSpace(run.LastErrorMessage)
            || !string.IsNullOrWhiteSpace(run.ResidualSummary)
            || LooksLikeFailure(run.VerificationStatus);
    }

    private static bool ShouldEmitExecutionStarted(TaskRun run, string state)
    {
        return string.Equals(state, WorkerMissionStates.Running, StringComparison.OrdinalIgnoreCase)
            || string.Equals(state, WorkerMissionStates.Verifying, StringComparison.OrdinalIgnoreCase)
            || string.Equals(state, WorkerMissionStates.Completed, StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrWhiteSpace(run.ApprovedByCaller)
                && (run.ChangedIds?.Count > 0 || run.ArtifactKeys?.Count > 0 || !string.IsNullOrWhiteSpace(run.VerificationStatus)));
    }

    private static bool LooksLikeCompleted(string value)
    {
        return value.IndexOf("complete", StringComparison.OrdinalIgnoreCase) >= 0
            || value.IndexOf("verified", StringComparison.OrdinalIgnoreCase) >= 0
            || value.IndexOf("done", StringComparison.OrdinalIgnoreCase) >= 0
            || value.IndexOf("success", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool LooksLikeFailure(string value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && (value.IndexOf("fail", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static bool LooksLikeCanceled(string value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && (value.IndexOf("cancel", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("reject", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static bool IsTerminalState(string state)
    {
        return string.Equals(state, WorkerMissionStates.Completed, StringComparison.OrdinalIgnoreCase)
            || string.Equals(state, WorkerMissionStates.Blocked, StringComparison.OrdinalIgnoreCase)
            || string.Equals(state, WorkerMissionStates.Failed, StringComparison.OrdinalIgnoreCase);
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty;
    }

    private static IEnumerable<string> EnumerateJsonFiles(string rootPath)
    {
        return Directory.Exists(rootPath)
            ? Directory.GetFiles(rootPath, "*.json", SearchOption.TopDirectoryOnly).OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            : Enumerable.Empty<string>();
    }

    private static string NormalizeUtc(DateTime value, DateTime fallback)
    {
        return (value == default ? fallback : value).ToUniversalTime().ToString("O");
    }

    private static void TrackSample(ImportBucket bucket, string sourceId)
    {
        if (bucket.SampleIds.Count >= 5 || string.IsNullOrWhiteSpace(sourceId))
        {
            return;
        }

        if (!bucket.SampleIds.Contains(sourceId, StringComparer.OrdinalIgnoreCase))
        {
            bucket.SampleIds.Add(sourceId);
        }
    }
}
