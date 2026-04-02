using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;
using BIM765T.Revit.Contracts.Serialization;
using BIM765T.Revit.Copilot.Core;
using BIM765T.Revit.WorkerHost.Configuration;
using BIM765T.Revit.WorkerHost.Eventing;
using BIM765T.Revit.WorkerHost.Health;
using BIM765T.Revit.WorkerHost.Kernel;
using BIM765T.Revit.WorkerHost.Memory;
using BIM765T.Revit.WorkerHost.Migration;
using BIM765T.Revit.WorkerHost.Routing;
using Xunit;

namespace BIM765T.Revit.WorkerHost.Tests;

public sealed class WorkerHostWave2Tests
{
    [Fact]
    public async Task LegacyStateMigrator_ImportsLegacyRunsAndMemory()
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

            var report = await migrator.MigrateAsync(force: false, dryRun: false, CancellationToken.None);

            Assert.True(report.Succeeded, string.Join(" || ", report.Errors));
            Assert.Equal(1, report.TaskRuns.Imported);
            Assert.Equal(1, report.Promotions.Imported);
            Assert.Equal(1, report.Episodes.Imported);
            Assert.Equal(1, report.QueueItems.Imported);

            var snapshot = await store.TryGetSnapshotAsync("legacy-run-01", CancellationToken.None);
            Assert.NotNull(snapshot);
            Assert.Equal(WorkerMissionStates.Completed, snapshot!.State);
            Assert.True(snapshot.Terminal);

            var events = await store.ListAsync("legacy-run-01", CancellationToken.None);
            Assert.Contains(events, x => x.EventType == "ApprovalRequested");
            Assert.Contains(events, x => x.EventType == "TaskCompleted");

            var hits = await store.SearchMemoryLexicalAsync("cassette", "doc-01", 10, CancellationToken.None);
            Assert.True(hits.Count >= 2);
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public void SafetyAgent_BlocksApprovalTokenMismatch()
    {
        var agent = new SafetyAgent();
        var snapshot = new MissionSnapshot
        {
            MissionId = "mission-01",
            State = WorkerMissionStates.AwaitingApproval,
            ApprovalToken = "approval-expected",
            PreviewRunId = "preview-01",
            ExpectedContextJson = """{"doc":"A"}"""
        };

        var assessment = agent.EvaluateCommand(new MissionCommandInput
        {
            MissionId = "mission-01",
            CommandName = "approval",
            AllowMutations = true,
            ApprovalToken = "approval-other",
            PreviewRunId = "preview-01",
            ExpectedContextJson = """{"doc":"A"}"""
        }, snapshot);

        Assert.False(assessment.Allowed);
        Assert.Equal(StatusCodes.ApprovalMismatch, assessment.StatusCode);
    }

    [Fact]
    public void SafetyAgent_BlocksApproval_WhenPreviewRunIdMissing()
    {
        var agent = new SafetyAgent();
        var snapshot = new MissionSnapshot
        {
            MissionId = "mission-01",
            State = WorkerMissionStates.AwaitingApproval,
            ApprovalToken = "approval-expected",
            PreviewRunId = "preview-01",
            ExpectedContextJson = """{"doc":"A"}"""
        };

        var assessment = agent.EvaluateCommand(new MissionCommandInput
        {
            MissionId = "mission-01",
            CommandName = "approval",
            AllowMutations = true,
            ApprovalToken = "approval-expected",
            PreviewRunId = string.Empty,
            ExpectedContextJson = """{"doc":"A"}"""
        }, snapshot);

        Assert.False(assessment.Allowed);
        Assert.Equal(StatusCodes.PreviewRunRequired, assessment.StatusCode);
    }

    [Fact]
    public void SafetyAgent_BlocksApproval_WhenExpectedContextMissing()
    {
        var agent = new SafetyAgent();
        var snapshot = new MissionSnapshot
        {
            MissionId = "mission-02",
            State = WorkerMissionStates.AwaitingApproval,
            ApprovalToken = "approval-expected",
            PreviewRunId = "preview-01",
            ExpectedContextJson = """{"doc":"A"}"""
        };

        var assessment = agent.EvaluateCommand(new MissionCommandInput
        {
            MissionId = "mission-02",
            CommandName = "approval",
            AllowMutations = true,
            ApprovalToken = "approval-expected",
            PreviewRunId = "preview-01",
            ExpectedContextJson = string.Empty
        }, snapshot);

        Assert.False(assessment.Allowed);
        Assert.Equal(StatusCodes.ContextMismatch, assessment.StatusCode);
    }

    [Fact]
    public async Task RuntimeHealthService_ReportsDegraded_WhenQdrantUnavailable()
    {
        var root = CreateTempDirectory();
        try
        {
            var settings = new WorkerHostSettings
            {
                StateRootPath = Path.Combine(root, "workerhost"),
                LegacyStateRootPath = Path.Combine(root, "legacy"),
                QdrantUrl = "http://127.0.0.1:1"
            };
            settings.EnsureCreated();

            var store = new SqliteMissionEventStore(settings.EventStorePath);
            var service = new RuntimeHealthService(
                settings,
                store,
                new StaticKernelClient(new KernelInvocationResult
                {
                    Succeeded = true,
                    StatusCode = StatusCodes.Ok,
                    PayloadJson = JsonUtil.Serialize(new SessionRuntimeHealthResponse())
                }),
                new ThrowingHttpClientFactory());

            var report = await service.CollectAsync(probePublicControlPlane: false, CancellationToken.None);

            Assert.True(report.Ready);
            Assert.True(report.StandaloneChatReady);
            Assert.True(report.LiveRevitReady);
            Assert.True(report.Degraded);
            Assert.True(report.Kernel.Reachable);
            Assert.False(report.Qdrant.Reachable);
            Assert.Contains("workerhost_public_control_plane + revit_private_kernel", report.RuntimeTopology, StringComparison.Ordinal);
            Assert.Contains("WorkerHost public control plane", report.ReadinessSummary, StringComparison.Ordinal);
            Assert.Contains(report.Diagnostics, x => x.Contains("canonical_public_ingress", StringComparison.Ordinal));
            Assert.Contains(report.Diagnostics, x => x.Contains("standalone_chat_ready: true", StringComparison.Ordinal));
            Assert.Contains(report.Diagnostics, x => x.Contains("live_revit_ready: true", StringComparison.Ordinal));
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
        var root = Path.Combine(Path.GetTempPath(), "BIM765T.WorkerHost.Wave2", Guid.NewGuid().ToString("N"));
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

    private sealed class StaticKernelClient : IKernelClient
    {
        private readonly KernelInvocationResult _result;

        public StaticKernelClient(KernelInvocationResult result)
        {
            _result = result;
        }

        public Task<KernelInvocationResult> InvokeAsync(KernelToolRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_result);
        }
    }

    private sealed class ThrowingHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new HttpClient(new ThrowingHttpMessageHandler(), disposeHandler: true);
        }
    }

    private sealed class ThrowingHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw new HttpRequestException("Qdrant offline for unit test.");
        }
    }
}
