using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;
using BIM765T.Revit.Contracts.Serialization;
using BIM765T.Revit.WorkerHost.Configuration;
using BIM765T.Revit.WorkerHost.Eventing;
using BIM765T.Revit.WorkerHost.Health;
using BIM765T.Revit.WorkerHost.Kernel;
using BIM765T.Revit.WorkerHost.Memory;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BIM765T.Revit.WorkerHost.Tests;

public sealed class WorkerHostWave4Tests
{
    [Fact]
    public async Task MemoryOutboxProjectorService_RequeuesStaleProcessingLease_And_CompletesProjection()
    {
        var root = CreateTempDirectory();
        try
        {
            var settings = new WorkerHostSettings
            {
                StateRootPath = Path.Combine(root, "workerhost"),
                LegacyStateRootPath = Path.Combine(root, "legacy"),
                QdrantUrl = "http://127.0.0.1:6333",
                OutboxLeaseTimeoutMs = 25,
                OutboxProjectorBatchSize = 4
            };
            settings.EnsureCreated();

            var store = new SqliteMissionEventStore(settings.EventStorePath);
            var lexicalMemory = new MemorySearchService(store, new ThrowingSemanticMemoryClient());
            var projector = new MemoryOutboxProjectorService(settings, store, lexicalMemory, NullLogger<MemoryOutboxProjectorService>.Instance);

            await AppendCompletedEventAsync(store, "mission-crash-01", "doc-crash");

            var lease = await store.LeaseOutboxBatchAsync("memory", 1, CancellationToken.None);
            var leased = Assert.Single(lease);
            Assert.Equal("processing", leased.Status);
            Assert.Equal(1, leased.AttemptCount);

            await Task.Delay(80);

            var processed = await projector.ProjectOnceAsync(CancellationToken.None);
            var outbox = await store.TryGetOutboxAsync("mission-crash-01", 1, "memory", CancellationToken.None);
            var stats = await store.GetStatisticsAsync(CancellationToken.None);
            var hits = await store.SearchMemoryLexicalAsync("mission-crash-01", "doc-crash", 5, CancellationToken.None);

            Assert.Equal(1, processed);
            Assert.NotNull(outbox);
            Assert.Equal("completed", outbox!.Status);
            Assert.Equal(2, outbox.AttemptCount);
            Assert.Equal(1, stats.CompletedOutboxCount);
            Assert.Equal(0, stats.ProcessingOutboxCount);
            Assert.Equal(0, stats.PendingOutboxCount);
            Assert.Single(hits);
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task MemoryOutboxProjectorService_MovesToDeadLetter_AfterMaxAttempts()
    {
        var root = CreateTempDirectory();
        try
        {
            var settings = new WorkerHostSettings
            {
                StateRootPath = Path.Combine(root, "workerhost"),
                LegacyStateRootPath = Path.Combine(root, "legacy"),
                QdrantUrl = "http://127.0.0.1:6333",
                OutboxProjectorBatchSize = 4,
                OutboxProjectorMaxAttempts = 2,
                OutboxProjectorBaseBackoffMs = 5,
                OutboxProjectorMaxBackoffMs = 20
            };
            settings.EnsureCreated();

            var store = new SqliteMissionEventStore(settings.EventStorePath);
            var failingMemory = new AlwaysFailMemorySearchService();
            var projector = new MemoryOutboxProjectorService(settings, store, failingMemory, NullLogger<MemoryOutboxProjectorService>.Instance);

            await AppendCompletedEventAsync(store, "mission-dead-01", "doc-dead");

            var processedRound1 = await projector.ProjectOnceAsync(CancellationToken.None);
            var firstAttempt = await store.TryGetOutboxAsync("mission-dead-01", 1, "memory", CancellationToken.None);

            Assert.Equal(0, processedRound1);
            Assert.NotNull(firstAttempt);
            Assert.Equal("pending", firstAttempt!.Status);
            Assert.Equal(1, firstAttempt.AttemptCount);
            Assert.False(string.IsNullOrWhiteSpace(firstAttempt.NextAttemptUtc));
            Assert.Contains("Simulated projector failure", firstAttempt.LastError, StringComparison.OrdinalIgnoreCase);

            await Task.Delay(40);

            var processedRound2 = await projector.ProjectOnceAsync(CancellationToken.None);
            var secondAttempt = await store.TryGetOutboxAsync("mission-dead-01", 1, "memory", CancellationToken.None);
            var stats = await store.GetStatisticsAsync(CancellationToken.None);

            Assert.Equal(0, processedRound2);
            Assert.NotNull(secondAttempt);
            Assert.Equal("dead_letter", secondAttempt!.Status);
            Assert.Equal(2, secondAttempt.AttemptCount);
            Assert.Contains("Simulated projector failure", secondAttempt.LastError, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(1, stats.DeadLetterOutboxCount);
            Assert.Equal(1, stats.FailedOutboxCount);
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RuntimeHealthService_ReportsDegraded_WhenDeadLetterOutboxExists()
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
            await AppendCompletedEventAsync(store, "mission-health-01", "doc-health");
            var lease = await store.LeaseOutboxBatchAsync("memory", 1, CancellationToken.None);
            var leased = Assert.Single(lease);
            await store.MoveOutboxToDeadLetterAsync(leased.OutboxId, "operator review required", CancellationToken.None);

            var service = new RuntimeHealthService(
                settings,
                store,
                new StaticKernelClient(new KernelInvocationResult
                {
                    Succeeded = true,
                    StatusCode = StatusCodes.Ok,
                    PayloadJson = JsonUtil.Serialize(new SessionRuntimeHealthResponse())
                }),
                new HealthyHttpClientFactory());

            var report = await service.CollectAsync(probePublicControlPlane: false, CancellationToken.None);

            Assert.True(report.Ready);
            Assert.True(report.Degraded);
            Assert.Equal(1, report.Store.DeadLetterOutboxCount);
            Assert.Contains(report.Diagnostics, x => x.IndexOf("dead-letter", StringComparison.OrdinalIgnoreCase) >= 0);
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task SqliteMissionEventStore_PruneOutboxAsync_Keeps_MaxEntries_PerTerminalStatus()
    {
        var root = CreateTempDirectory();
        try
        {
            var databasePath = Path.Combine(root, "workerhost.sqlite");
            var store = new SqliteMissionEventStore(databasePath);
            await SeedTerminalOutboxRowsAsync(databasePath, "completed", 3, DateTime.UtcNow.AddDays(-10));
            await SeedTerminalOutboxRowsAsync(databasePath, "ignored", 3, DateTime.UtcNow.AddDays(-10));
            await SeedTerminalOutboxRowsAsync(databasePath, "dead_letter", 3, DateTime.UtcNow.AddDays(-10));

            var pruned = await store.PruneOutboxAsync(retentionDays: 7, maxEntriesPerStatus: 2, CancellationToken.None);
            var stats = await store.GetStatisticsAsync(CancellationToken.None);

            Assert.Equal(3, pruned);
            Assert.Equal(2, stats.CompletedOutboxCount);
            Assert.Equal(2, stats.IgnoredOutboxCount);
            Assert.Equal(2, stats.DeadLetterOutboxCount);
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    private static async Task AppendCompletedEventAsync(SqliteMissionEventStore store, string missionId, string documentKey)
    {
        await store.AppendAsync(
            new MissionEventRecord
            {
                StreamId = missionId,
                EventType = "TaskCompleted",
                PayloadJson = """{"summary":"project me"}""",
                OccurredUtc = DateTime.UtcNow.ToString("O"),
                CorrelationId = "corr-" + missionId,
                CausationId = "cause-" + missionId,
                ActorId = "tester",
                DocumentKey = documentKey,
                Terminal = true
            },
            System.Text.Json.JsonSerializer.Serialize(new MissionSnapshot
            {
                MissionId = missionId,
                State = WorkerMissionStates.Completed,
                Terminal = true,
                Version = 1
            }),
            CancellationToken.None);
    }

    private static string CreateTempDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "BIM765T.WorkerHost.Wave4", Guid.NewGuid().ToString("N"));
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

    private sealed class AlwaysFailMemorySearchService : IMemorySearchService
    {
        public Task UpsertAsync(PromotedMemoryRecord record, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Simulated projector failure.");
        }

        public Task<IReadOnlyList<SemanticMemoryHit>> SearchAsync(string query, string documentKey, int topK, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<SemanticMemoryHit>>(Array.Empty<SemanticMemoryHit>());
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

    private sealed class HealthyHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new HttpClient(new HealthyHttpMessageHandler(), disposeHandler: true);
        }
    }

    private sealed class HealthyHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}")
            });
        }
    }

    private static async Task SeedTerminalOutboxRowsAsync(string databasePath, string status, int count, DateTime firstAttemptUtc)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString());
        await connection.OpenAsync();

        for (var index = 0; index < count; index++)
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO outbox(event_stream_id, event_version, target, status, attempt_count, leased_utc, next_attempt_utc, last_error, last_attempt_utc)
                VALUES($streamId, $eventVersion, 'memory', $status, 1, '', '', '', $lastAttemptUtc);
                """;
            command.Parameters.AddWithValue("$streamId", $"{status}-mission-{index}");
            command.Parameters.AddWithValue("$eventVersion", index + 1);
            command.Parameters.AddWithValue("$status", status);
            command.Parameters.AddWithValue("$lastAttemptUtc", firstAttemptUtc.AddMinutes(index).ToString("O"));
            await command.ExecuteNonQueryAsync();
        }
    }
}
