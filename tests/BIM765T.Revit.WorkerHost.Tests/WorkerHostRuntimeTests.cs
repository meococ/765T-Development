using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;
using BIM765T.Revit.Contracts.Proto;
using BIM765T.Revit.Contracts.Serialization;
using BIM765T.Revit.WorkerHost.Eventing;
using BIM765T.Revit.WorkerHost.Kernel;
using BIM765T.Revit.WorkerHost.Memory;
using BIM765T.Revit.WorkerHost.Routing;
using Microsoft.Extensions.Logging;
using Xunit;

namespace BIM765T.Revit.WorkerHost.Tests;

public sealed class WorkerHostRuntimeTests
{
    [Fact]
    public async Task SqliteMissionEventStore_AppendsEvents_And_RehydratesLatestSnapshot()
    {
        var databasePath = CreateTempDatabasePath();
        try
        {
            var store = new SqliteMissionEventStore(databasePath);
            var snapshot = new MissionSnapshot
            {
                MissionId = "mission-01",
                SessionId = "session-01",
                State = WorkerMissionStates.Running,
                ResponseText = "preview"
            };

            await store.AppendAsync(
                new MissionEventRecord
                {
                    StreamId = "mission-01",
                    EventType = "TaskStarted",
                    PayloadJson = """{"step":"start"}""",
                    OccurredUtc = DateTime.UtcNow.ToString("O"),
                    CorrelationId = "corr-01",
                    CausationId = "cause-01",
                    ActorId = "tester",
                    DocumentKey = "doc-01"
                },
                System.Text.Json.JsonSerializer.Serialize(snapshot),
                CancellationToken.None);

            snapshot.State = WorkerMissionStates.Completed;
            snapshot.ResponseText = "done";
            snapshot.Terminal = true;

            await store.AppendAsync(
                new MissionEventRecord
                {
                    StreamId = "mission-01",
                    EventType = "TaskCompleted",
                    PayloadJson = """{"step":"complete"}""",
                    OccurredUtc = DateTime.UtcNow.ToString("O"),
                    CorrelationId = "corr-01",
                    CausationId = "cause-02",
                    ActorId = "tester",
                    DocumentKey = "doc-01",
                    Terminal = true
                },
                System.Text.Json.JsonSerializer.Serialize(snapshot),
                CancellationToken.None);

            var events = await store.ListAsync("mission-01", CancellationToken.None);
            var rehydrated = await store.TryGetSnapshotAsync("mission-01", CancellationToken.None);
            await store.CheckpointAsync(CancellationToken.None);

            Assert.Equal(2, events.Count);
            Assert.Equal(new[] { "TaskStarted", "TaskCompleted" }, events.Select(x => x.EventType).ToArray());
            Assert.NotNull(rehydrated);
            Assert.Equal(WorkerMissionStates.Completed, rehydrated!.State);
            Assert.True(rehydrated.Terminal);
            Assert.Equal("done", rehydrated.ResponseText);
        }
        finally
        {
            SafeDelete(databasePath);
        }
    }

    [Fact]
    public async Task MemorySearchService_FallsBackToLexical_WhenSemanticClientFails()
    {
        var databasePath = CreateTempDatabasePath();
        try
        {
            var store = new SqliteMissionEventStore(databasePath);
            var service = new MemorySearchService(store, new ThrowingSemanticMemoryClient());

            await service.UpsertAsync(
                new PromotedMemoryRecord
                {
                    MemoryId = "mem-01",
                    Kind = "lesson",
                    Title = "Checkpoint recovery",
                    Snippet = "Replay event stream after crash.",
                    SourceRef = "docs/agent/LESSONS_LEARNED.md",
                    DocumentKey = "doc-01",
                    EventType = "MemoryPromoted",
                    RunId = "run-01",
                    Promoted = true,
                    PayloadJson = """{"pattern":"replay"}""",
                    CreatedUtc = DateTime.UtcNow.ToString("O")
                },
                CancellationToken.None);

            await store.UpsertMemoryAsync(
                new PromotedMemoryRecord
                {
                    MemoryId = "mem-02",
                    Kind = "lesson",
                    Title = "Other document",
                    Snippet = "Should not appear for doc-01.",
                    SourceRef = "docs/agent/LESSONS_LEARNED.md",
                    DocumentKey = "doc-02",
                    EventType = "MemoryPromoted",
                    RunId = "run-02",
                    Promoted = true,
                    PayloadJson = """{"pattern":"other"}""",
                    CreatedUtc = DateTime.UtcNow.ToString("O")
                },
                CancellationToken.None);

            var hits = await service.SearchAsync("Replay", "doc-01", 5, CancellationToken.None);

            var hit = Assert.Single(hits);
            Assert.Equal("mem-01", hit.Id);
            Assert.Equal("Checkpoint recovery", hit.Title);
            Assert.Equal("docs/agent/LESSONS_LEARNED.md", hit.SourceRef);
        }
        finally
        {
            SafeDelete(databasePath);
        }
    }

    [Fact]
    public async Task MemorySearchService_LogsWarning_WhenSemanticUpsertFails()
    {
        var databasePath = CreateTempDatabasePath();
        try
        {
            var store = new SqliteMissionEventStore(databasePath);
            var logger = new CapturingLogger<MemorySearchService>();
            var service = new MemorySearchService(store, new ThrowingSemanticMemoryClient(), logger);

            await service.UpsertAsync(
                new PromotedMemoryRecord
                {
                    MemoryId = "mem-log-01",
                    Kind = "lesson",
                    Title = "Checkpoint recovery",
                    Snippet = "Replay event stream after crash.",
                    SourceRef = "docs/agent/LESSONS_LEARNED.md",
                    DocumentKey = "doc-log-01",
                    EventType = "MemoryPromoted",
                    RunId = "run-log-01",
                    Promoted = true,
                    PayloadJson = """{"pattern":"replay"}""",
                    CreatedUtc = DateTime.UtcNow.ToString("O")
                },
                CancellationToken.None);

            Assert.Contains(logger.Entries, x =>
                x.LogLevel == LogLevel.Warning
                && x.Message.IndexOf("Qdrant upsert failed", StringComparison.OrdinalIgnoreCase) >= 0
                && x.Message.IndexOf("mem-log-01", StringComparison.OrdinalIgnoreCase) >= 0);
        }
        finally
        {
            SafeDelete(databasePath);
        }
    }

    [Fact]
    public void MissionToolCandidateBuilder_ShipMode_Broadens_Project_And_Script_Tooling()
    {
        var builder = new MissionToolCandidateBuilder();

        var candidates = builder.Build(new MissionPlanningContext
        {
            UserMessage = "hay init workspace, quet du an, de xuat script dynamo va tao tool cho schedule",
            AutonomyMode = WorkerAutonomyModes.Ship,
            DocumentKey = "doc-ship"
        });

        Assert.Equal(WorkerAutonomyModes.Ship, candidates.AutonomyMode);
        Assert.Contains(ToolNames.ProjectInitPreview, candidates.CandidateTools);
        Assert.Contains(ToolNames.ProjectDeepScan, candidates.CandidateTools);
        Assert.Contains(ToolNames.ScriptComposeSafe, candidates.CandidateTools);
        Assert.Contains(ToolNames.ScriptValidate, candidates.CandidateTools);
        Assert.Contains(ToolNames.ScriptComposeSafe, candidates.CandidateCommands);
    }

    [Fact]
    public async Task MissionOrchestrator_SubmitLocalConversationAsync_AppendsLifecycleEvents_WithoutKernel()
    {
        var databasePath = CreateTempDatabasePath();
        try
        {
            var store = new SqliteMissionEventStore(databasePath);
            var memory = new MemorySearchService(store, new ThrowingSemanticMemoryClient());
            var kernel = new QueueKernelClient();
            var orchestrator = new MissionOrchestrator(
                store,
                new InMemoryMissionEventBus(),
                new BIM765T.Revit.WorkerHost.Configuration.WorkerHostSettings(),
                kernel,
                new PlannerAgent(),
                new RetrieverAgent(memory),
                new ExecutionPolicyEvaluator(new SafetyAgent()),
                new VerifierAgent());
            var meta = new EnvelopeMetadata
            {
                MissionId = "mission-local-01",
                SessionId = "session-local-01",
                CorrelationId = "corr-local-01",
                ActorId = "tester",
                RequestedAtUtc = DateTime.UtcNow.ToString("O")
            };
            var retrieval = await orchestrator.RetrieveAsync("hello", string.Empty, CancellationToken.None);
            var plan = orchestrator.BuildSubmissionPlan(
                "hello",
                "revit_worker",
                WorkerClientSurfaces.Mcp,
                continueMission: false,
                meta,
                retrieval);
            var workerResponse = new WorkerResponse
            {
                SessionId = "session-local-01",
                MissionId = "mission-local-01",
                MissionStatus = WorkerMissionStates.Completed,
                Messages = new List<WorkerChatMessage>
                {
                    new WorkerChatMessage { Content = "Standalone reply." }
                }
            };

            var result = await orchestrator.SubmitLocalConversationAsync(
                "mission-local-01",
                "{}",
                "hello",
                "revit_worker",
                WorkerClientSurfaces.Mcp,
                continueMission: false,
                meta,
                plan,
                retrieval,
                workerResponse,
                CancellationToken.None);

            Assert.Equal(WorkerMissionStates.Completed, result.Snapshot.State);
            Assert.True(result.Snapshot.Terminal);
            Assert.Equal("Standalone reply.", result.Snapshot.ResponseText);
            Assert.Equal(StatusCodes.Ok, result.KernelResult.StatusCode);
            Assert.Empty(kernel.Requests);
            Assert.Contains(result.Events, x => x.EventType == "TaskStarted");
            Assert.Contains(result.Events, x => x.EventType == "TaskCompleted");
        }
        finally
        {
            SafeDelete(databasePath);
        }
    }

    [Fact]
    public async Task MissionOrchestrator_SubmitAndApproveMission_AppendsEnterpriseLifecycleEvents()
    {
        var databasePath = CreateTempDatabasePath();
        try
        {
            var store = new SqliteMissionEventStore(databasePath);
            var memory = new MemorySearchService(store, new ThrowingSemanticMemoryClient());
            await memory.UpsertAsync(
                new PromotedMemoryRecord
                {
                    MemoryId = "mem-01",
                    Kind = "lesson",
                    Title = "Approval flow",
                    Snippet = "Always preview before execute.",
                    SourceRef = "docs/agent/PROJECT_MEMORY.md",
                    DocumentKey = string.Empty,
                    EventType = "MemoryPromoted",
                    RunId = string.Empty,
                    Promoted = true,
                    PayloadJson = """{"rule":"preview"}""",
                    CreatedUtc = DateTime.UtcNow.ToString("O")
                },
                CancellationToken.None);

            var kernel = new QueueKernelClient(
                new KernelInvocationResult
                {
                    Succeeded = true,
                    ConfirmationRequired = true,
                    StatusCode = StatusCodes.TaskApprovalRequired,
                    ApprovalToken = "approval-01",
                    PreviewRunId = "preview-01",
                    PayloadJson = JsonUtil.Serialize(new WorkerResponse
                    {
                        MissionId = "mission-01",
                        MissionStatus = WorkerMissionStates.AwaitingApproval,
                        Messages = new List<WorkerChatMessage>
                        {
                            new WorkerChatMessage { Content = "Executed safely." }
                        }
                    }),
                    ProtocolVersion = BridgeProtocol.PipeV1
                },
                new KernelInvocationResult
                {
                    Succeeded = true,
                    ConfirmationRequired = false,
                    StatusCode = StatusCodes.Ok,
                    PayloadJson = JsonUtil.Serialize(new WorkerResponse
                    {
                        MissionId = "mission-01",
                        MissionStatus = WorkerMissionStates.Completed,
                        Messages = new List<WorkerChatMessage>
                        {
                            new WorkerChatMessage { Content = "Executed safely." }
                        }
                    }),
                    ProtocolVersion = BridgeProtocol.PipeV1
                });

            var orchestrator = new MissionOrchestrator(
                store,
                new InMemoryMissionEventBus(),
                new BIM765T.Revit.WorkerHost.Configuration.WorkerHostSettings(),
                kernel,
                new PlannerAgent(),
                new RetrieverAgent(memory),
                new ExecutionPolicyEvaluator(new SafetyAgent()),
                new VerifierAgent());
            var meta = new EnvelopeMetadata
            {
                MissionId = "mission-01",
                SessionId = "session-01",
                CorrelationId = "corr-01",
                ActorId = "tester",
                DocumentKey = "doc-01",
                RequestedAtUtc = DateTime.UtcNow.ToString("O")
            };

            var submit = await orchestrator.SubmitMissionAsync(
                "mission-01",
                """{"message":"approve this task"}""",
                "Tao preview roi cho approve",
                "revit_worker",
                WorkerClientSurfaces.Mcp,
                continueMission: false,
                meta,
                CancellationToken.None);

            Assert.Equal(WorkerMissionStates.AwaitingApproval, submit.Snapshot.State);
            Assert.False(submit.Snapshot.Terminal);
            Assert.Equal("approval-01", submit.Snapshot.ApprovalToken);
            Assert.Contains(submit.Events, x => x.EventType == "IntentClassified");
            Assert.Contains(submit.Events, x => x.EventType == "ContextResolved");
            Assert.Contains(submit.Events, x => x.EventType == "PreviewGenerated");
            Assert.Contains(submit.Events, x => x.EventType == "ApprovalRequested");

            var approve = await orchestrator.ApproveMissionAsync(new MissionCommandInput
            {
                Meta = meta,
                MissionId = "mission-01",
                CommandName = "approval",
                ApprovalToken = "approval-01",
                PreviewRunId = "preview-01",
                AllowMutations = true
            }, CancellationToken.None);

            Assert.Equal(WorkerMissionStates.Completed, approve.Snapshot.State);
            Assert.True(approve.Snapshot.Terminal);
            Assert.Equal("Executed safely.", approve.Snapshot.ResponseText);
            Assert.Contains(approve.Events, x => x.EventType == "UserApproved");
            Assert.Contains("approval", kernel.Requests[1].PayloadJson, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(2, kernel.Requests.Count);
        }
        finally
        {
            SafeDelete(databasePath);
        }
    }

    private static string CreateTempDatabasePath()
    {
        var directory = Path.Combine(Path.GetTempPath(), "BIM765T.WorkerHost.Tests");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, Guid.NewGuid().ToString("N") + ".sqlite");
    }

    private static void SafeDelete(string databasePath)
    {
        try
        {
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }

            var wal = databasePath + "-wal";
            if (File.Exists(wal))
            {
                File.Delete(wal);
            }

            var shm = databasePath + "-shm";
            if (File.Exists(shm))
            {
                File.Delete(shm);
            }
        }
        catch
        {
            // Ignore temp cleanup failures.
        }
    }

    private sealed class ThrowingSemanticMemoryClient : ISemanticMemoryClient
    {
        public Task EnsureReadyAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task UpsertAsync(PromotedMemoryRecord record, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Qdrant unavailable in unit test.");
        }

        public Task<IReadOnlyList<SemanticMemoryHit>> SearchAsync(string query, string documentKey, int topK, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Qdrant unavailable in unit test.");
        }
    }

    private sealed class QueueKernelClient : IKernelClient
    {
        private readonly Queue<KernelInvocationResult> _responses;

        public QueueKernelClient(params KernelInvocationResult[] responses)
        {
            _responses = new Queue<KernelInvocationResult>(responses);
        }

        public List<KernelToolRequest> Requests { get; } = new List<KernelToolRequest>();

        public Task<KernelInvocationResult> InvokeAsync(KernelToolRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(_responses.Dequeue());
        }
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = new();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(logLevel, formatter(state, exception)));
        }

        public sealed record LogEntry(LogLevel LogLevel, string Message);

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }
}

