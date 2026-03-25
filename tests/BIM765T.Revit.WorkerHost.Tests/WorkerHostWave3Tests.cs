using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BIM765T.Revit.Contracts.Platform;
using BIM765T.Revit.Contracts.Serialization;
using BIM765T.Revit.Copilot.Core;
using BIM765T.Revit.WorkerHost.Configuration;
using BIM765T.Revit.WorkerHost.Eventing;
using BIM765T.Revit.WorkerHost.Memory;
using BIM765T.Revit.WorkerHost.Migration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BIM765T.Revit.WorkerHost.Tests;

public sealed class WorkerHostWave3Tests
{
    [Fact]
    public async Task TryGetSnapshotAsync_Replays_WhenSnapshotMissing()
    {
        var root = CreateTempDirectory();
        try
        {
            var databasePath = Path.Combine(root, "workerhost.sqlite");
            var store = new SqliteMissionEventStore(databasePath);
            var missionId = "mission-replay-01";
            var occurredUtc = DateTime.UtcNow.ToString("O");

            var events = new List<MissionEventRecord>
            {
                new MissionEventRecord
                {
                    StreamId = missionId,
                    EventType = "TaskStarted",
                    PayloadJson = """{"input":"open cassette"}""",
                    OccurredUtc = occurredUtc,
                    CorrelationId = "corr-replay-01",
                    CausationId = "cause-replay-01",
                    ActorId = "tester",
                    DocumentKey = "doc-replay"
                },
                new MissionEventRecord
                {
                    StreamId = missionId,
                    EventType = "ApprovalRequested",
                    PayloadJson = """{"approvalToken":"approval-replay-01","previewRunId":"preview-replay-01","expectedContextJson":"{\"doc\":\"A\"}"}""",
                    OccurredUtc = occurredUtc,
                    CorrelationId = "corr-replay-01",
                    CausationId = "cause-replay-02",
                    ActorId = "tester",
                    DocumentKey = "doc-replay"
                },
                new MissionEventRecord
                {
                    StreamId = missionId,
                    EventType = "UserApproved",
                    PayloadJson = """{"approvalToken":"approval-replay-01"}""",
                    OccurredUtc = occurredUtc,
                    CorrelationId = "corr-replay-01",
                    CausationId = "cause-replay-03",
                    ActorId = "tester",
                    DocumentKey = "doc-replay"
                },
                new MissionEventRecord
                {
                    StreamId = missionId,
                    EventType = "TaskCompleted",
                    PayloadJson = """{"status":"OK","response":"done"}""",
                    OccurredUtc = occurredUtc,
                    CorrelationId = "corr-replay-01",
                    CausationId = "cause-replay-04",
                    ActorId = "tester",
                    DocumentKey = "doc-replay",
                    Terminal = true
                }
            };

            var originalSnapshot = new MissionSnapshot
            {
                MissionId = missionId,
                State = WorkerMissionStates.Completed,
                ApprovalToken = "approval-replay-01",
                PreviewRunId = "preview-replay-01",
                ExpectedContextJson = """{"doc":"A"}""",
                ResponseJson = """{"response":"done"}""",
                LastStatusCode = "OK",
                Terminal = true,
                Version = 4
            };

            await store.AppendBatchAsync(events, System.Text.Json.JsonSerializer.Serialize(originalSnapshot), CancellationToken.None);
            await store.DeleteSnapshotAsync(missionId, CancellationToken.None);

            var replayed = await store.TryGetSnapshotAsync(missionId, CancellationToken.None);
            var stats = await store.GetStatisticsAsync(CancellationToken.None);

            Assert.NotNull(replayed);
            Assert.Equal(WorkerMissionStates.Completed, replayed!.State);
            Assert.True(replayed.Terminal);
            Assert.Equal("approval-replay-01", replayed.ApprovalToken);
            Assert.Equal("preview-replay-01", replayed.PreviewRunId);
            Assert.Equal("""{"doc":"A"}""", replayed.ExpectedContextJson);
            Assert.Equal("OK", replayed.LastStatusCode);
            Assert.Equal(1, stats.SnapshotCount);
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task MemoryOutboxProjectorService_ProjectsPendingEvents_And_CompletesOutbox()
    {
        var root = CreateTempDirectory();
        try
        {
            var settings = new WorkerHostSettings
            {
                StateRootPath = Path.Combine(root, "workerhost"),
                LegacyStateRootPath = Path.Combine(root, "legacy"),
                QdrantUrl = "http://127.0.0.1:6333"
            };
            settings.EnsureCreated();

            var store = new SqliteMissionEventStore(settings.EventStorePath);
            var memory = new MemorySearchService(store, new ThrowingSemanticMemoryClient());
            var projector = new MemoryOutboxProjectorService(settings, store, memory, NullLogger<MemoryOutboxProjectorService>.Instance);

            await store.AppendAsync(
                new MissionEventRecord
                {
                    StreamId = "mission-outbox-01",
                    EventType = "TaskCompleted",
                    PayloadJson = """{"summary":"cassette projected"}""",
                    OccurredUtc = DateTime.UtcNow.ToString("O"),
                    CorrelationId = "corr-outbox-01",
                    CausationId = "cause-outbox-01",
                    ActorId = "tester",
                    DocumentKey = "doc-outbox",
                    Terminal = true
                },
                System.Text.Json.JsonSerializer.Serialize(new MissionSnapshot
                {
                    MissionId = "mission-outbox-01",
                    State = WorkerMissionStates.Completed,
                    Terminal = true,
                    Version = 1
                }),
                CancellationToken.None);

            var before = await store.GetStatisticsAsync(CancellationToken.None);
            var processed = await projector.ProjectOnceAsync(CancellationToken.None);
            var after = await store.GetStatisticsAsync(CancellationToken.None);
            var hits = await store.SearchMemoryLexicalAsync("mission-outbox-01", "doc-outbox", 5, CancellationToken.None);

            Assert.Equal(1, before.PendingOutboxCount);
            Assert.Equal(1, processed);
            Assert.Equal(0, after.PendingOutboxCount);
            Assert.Equal(1, after.CompletedOutboxCount);
            Assert.True(after.MemoryProjectionCount >= 1);
            var hit = Assert.Single(hits);
            Assert.Equal("event://mission-outbox-01/1", hit.SourceRef);
            Assert.Equal("TaskCompleted", hit.EventType);
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task LegacyStateMigrator_DryRun_DoesNotMutateStore()
    {
        var root = CreateTempDirectory();
        try
        {
            var legacyRoot = Path.Combine(root, "legacy");
            var workerHostRoot = Path.Combine(root, "workerhost");
            var settings = new WorkerHostSettings
            {
                StateRootPath = workerHostRoot,
                LegacyStateRootPath = legacyRoot,
                QdrantUrl = "http://127.0.0.1:6333"
            };
            settings.EnsureCreated();

            var legacyPaths = new CopilotStatePaths(legacyRoot);
            WriteLegacyRun(legacyPaths);
            WriteLegacyPromotion(legacyPaths);
            WriteLegacyEpisode(legacyPaths);
            WriteLegacyQueueItem(legacyPaths);

            var store = new SqliteMissionEventStore(settings.EventStorePath);
            var memory = new MemorySearchService(store, new ThrowingSemanticMemoryClient());
            var migrator = new LegacyStateMigrator(settings, store, memory);

            var report = await migrator.MigrateAsync(force: false, dryRun: true, CancellationToken.None);
            var stats = await store.GetStatisticsAsync(CancellationToken.None);
            var snapshot = await store.TryGetSnapshotAsync("legacy-run-01", CancellationToken.None);

            Assert.True(report.DryRun);
            Assert.True(report.Succeeded, string.Join(" || ", report.Errors));
            Assert.Equal(1, report.TaskRuns.WouldImport);
            Assert.Equal(1, report.Promotions.WouldImport);
            Assert.Equal(1, report.Episodes.WouldImport);
            Assert.Equal(1, report.QueueItems.WouldImport);
            Assert.Equal(0, report.TaskRuns.Imported);
            Assert.Equal(0, report.Promotions.Imported);
            Assert.Equal(0, report.Episodes.Imported);
            Assert.Equal(0, report.QueueItems.Imported);
            Assert.Equal(0, stats.EventCount);
            Assert.Equal(0, stats.MemoryProjectionCount);
            Assert.Equal(0, stats.MigrationCount);
            Assert.Null(snapshot);
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    private static void WriteLegacyRun(CopilotStatePaths paths)
    {
        var run = new TaskRun
        {
            RunId = "legacy-run-01",
            TaskKind = "workflow",
            TaskName = "round.penetration",
            Status = "completed",
            DocumentKey = "doc-01",
            IntentSummary = "Externalize cassette opening",
            PlanSummary = "Preview -> approval -> execute",
            InputJson = """{"task":"round"}""",
            ApprovalToken = "approval-01",
            PreviewRunId = "preview-01",
            ApprovedByCaller = "tester",
            ApprovedBySessionId = "session-01",
            VerificationStatus = "passed",
            ChangedIds = new List<int> { 101, 102 },
            ArtifactKeys = new List<string> { "artifacts/packet.json" },
            CreatedUtc = DateTime.UtcNow.AddMinutes(-20),
            UpdatedUtc = DateTime.UtcNow.AddMinutes(-5)
        };

        File.WriteAllText(paths.GetRunPath(run.RunId), JsonUtil.Serialize(run));
    }

    private static void WriteLegacyPromotion(CopilotStatePaths paths)
    {
        var record = new TaskMemoryPromotionRecord
        {
            PromotionId = "promotion-01",
            RunId = "legacy-run-01",
            PromotionKind = "lesson",
            Summary = "Cassette opening needs approval token replay discipline.",
            Notes = "Always keep preview_run_id when resuming.",
            DocumentKey = "doc-01",
            TaskKind = "workflow",
            TaskName = "round.penetration",
            CreatedUtc = DateTime.UtcNow.AddMinutes(-10),
            MemoryRecord = new MemoryRecord
            {
                Kind = "lesson",
                Summary = "cassette approval replay"
            }
        };

        File.WriteAllText(paths.GetPromotionPath(record.PromotionId), JsonUtil.Serialize(record));
    }

    private static void WriteLegacyEpisode(CopilotStatePaths paths)
    {
        var record = new EpisodicRecord
        {
            EpisodeId = "episode-01",
            RunId = "legacy-run-01",
            MissionType = "round.penetration",
            Outcome = "cassette clash resolved",
            KeyObservations = new List<string> { "cassette", "preview", "approval" },
            KeyDecisions = new List<string> { "rerun preview before execute" },
            ToolSequence = new List<string> { "worker.message", "task.preview", "task.approve_step" },
            DocumentKey = "doc-01",
            CreatedUtc = DateTime.UtcNow.AddMinutes(-8)
        };

        File.WriteAllText(paths.GetWorkerEpisodePath(record.EpisodeId), JsonUtil.Serialize(record));
    }

    private static void WriteLegacyQueueItem(CopilotStatePaths paths)
    {
        var item = new TaskQueueItem
        {
            QueueItemId = "queue-01",
            RunId = "legacy-run-01",
            TaskName = "round.penetration",
            QueueName = "approved",
            Status = "completed",
            DocumentKey = "doc-01",
            Note = "cassette queue item",
            UpdatedUtc = DateTime.UtcNow.AddMinutes(-4)
        };

        File.WriteAllText(paths.GetTaskQueueItemPath(item.QueueItemId), JsonUtil.Serialize(item));
    }

    private static string CreateTempDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "BIM765T.WorkerHost.Wave3", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void SafeDeleteDirectory(string root)
    {
        try
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
        catch
        {
            // ignore temp cleanup failures
        }
    }

    private sealed class ThrowingSemanticMemoryClient : ISemanticMemoryClient
    {
        public Task EnsureReadyAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task UpsertAsync(PromotedMemoryRecord record, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Semantic memory is intentionally unavailable in this test.");
        }

        public Task<IReadOnlyList<SemanticMemoryHit>> SearchAsync(string query, string documentKey, int topK, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Semantic memory is intentionally unavailable in this test.");
        }
    }
}
