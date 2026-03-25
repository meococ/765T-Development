using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BIM765T.Revit.Contracts.Platform;
using BIM765T.Revit.WorkerHost.Configuration;
using BIM765T.Revit.WorkerHost.Eventing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BIM765T.Revit.WorkerHost.Memory;

internal sealed class MemoryOutboxProjectorService : BackgroundService
{
    private static readonly Action<ILogger, Exception?> LoopFailureLog =
        LoggerMessage.Define(LogLevel.Warning, new EventId(8101, nameof(MemoryOutboxProjectorService)), "Memory outbox projector loop failed.");

    private static readonly Action<ILogger, long, string, Exception?> ProjectionFailureLog =
        LoggerMessage.Define<long, string>(LogLevel.Warning, new EventId(8102, nameof(MemoryOutboxProjectorService)), "Failed to project outbox item {OutboxId} for stream {StreamId}.");

    private static readonly Action<ILogger, int, Exception?> RequeuedStaleLeaseLog =
        LoggerMessage.Define<int>(LogLevel.Information, new EventId(8103, nameof(MemoryOutboxProjectorService)), "Re-queued {Count} stale processing outbox item(s).");

    private static readonly Action<ILogger, long, int, string, Exception?> RetryScheduledLog =
        LoggerMessage.Define<long, int, string>(LogLevel.Warning, new EventId(8104, nameof(MemoryOutboxProjectorService)), "Scheduled retry for outbox item {OutboxId} at attempt {AttemptCount}. Next attempt at {NextAttemptUtc}.");

    private static readonly Action<ILogger, long, int, Exception?> DeadLetterLog =
        LoggerMessage.Define<long, int>(LogLevel.Error, new EventId(8105, nameof(MemoryOutboxProjectorService)), "Moved outbox item {OutboxId} to dead-letter after {AttemptCount} attempts.");

    private static readonly Action<ILogger, int, Exception?> OutboxPrunedLog =
        LoggerMessage.Define<int>(LogLevel.Information, new EventId(8106, nameof(MemoryOutboxProjectorService)), "Pruned {Count} old outbox entries.");

    // Prune every N poll cycles
    private const int PruneIntervalCycles = 60;

    private readonly WorkerHostSettings _settings;
    private readonly SqliteMissionEventStore _store;
    private readonly IMemorySearchService _memorySearch;
    private readonly ILogger<MemoryOutboxProjectorService> _logger;
    private int _pruneCounter;

    public MemoryOutboxProjectorService(
        WorkerHostSettings settings,
        SqliteMissionEventStore store,
        IMemorySearchService memorySearch,
        ILogger<MemoryOutboxProjectorService> logger)
    {
        _settings = settings;
        _store = store;
        _memorySearch = memorySearch;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var projected = await ProjectOnceAsync(stoppingToken).ConfigureAwait(false);

                // Periodic outbox pruning to prevent unbounded growth
                if (++_pruneCounter >= PruneIntervalCycles)
                {
                    _pruneCounter = 0;
                    var pruned = await _store.PruneOutboxAsync(
                        retentionDays: 7,
                        maxEntriesPerStatus: 1000,
                        stoppingToken).ConfigureAwait(false);
                    if (pruned > 0)
                    {
                        OutboxPrunedLog(_logger, pruned, null);
                    }
                }

                if (projected == 0)
                {
                    await Task.Delay(_settings.OutboxProjectorPollIntervalMs, stoppingToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                LoopFailureLog(_logger, ex);
                await Task.Delay(_settings.OutboxProjectorPollIntervalMs, stoppingToken).ConfigureAwait(false);
            }
        }
    }

    internal async Task<int> ProjectOnceAsync(CancellationToken cancellationToken)
    {
        var reclaimed = await _store.RequeueStaleProcessingAsync(
            "memory",
            TimeSpan.FromMilliseconds(Math.Max(1, _settings.OutboxLeaseTimeoutMs)),
            cancellationToken).ConfigureAwait(false);
        if (reclaimed > 0)
        {
            RequeuedStaleLeaseLog(_logger, reclaimed, null);
        }

        var leased = await _store.LeaseOutboxBatchAsync("memory", _settings.OutboxProjectorBatchSize, cancellationToken).ConfigureAwait(false);
        var processed = 0;
        foreach (var outbox in leased)
        {
            try
            {
                var record = await _store.TryGetEventAsync(outbox.StreamId, outbox.EventVersion, cancellationToken).ConfigureAwait(false);
                if (record == null)
                {
                    await HandleFailureAsync(outbox, "Outbox event payload was not found.", cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var memory = BuildMemoryProjection(record);
                if (memory != null)
                {
                    await _memorySearch.UpsertAsync(memory, cancellationToken).ConfigureAwait(false);
                    await _store.CompleteOutboxAsync(outbox.OutboxId, "completed", cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await _store.CompleteOutboxAsync(outbox.OutboxId, "ignored", cancellationToken).ConfigureAwait(false);
                }

                processed++;
            }
            catch (Exception ex)
            {
                ProjectionFailureLog(_logger, outbox.OutboxId, outbox.StreamId, ex);
                await HandleFailureAsync(outbox, ex.Message, cancellationToken).ConfigureAwait(false);
            }
        }

        return processed;
    }

    private async Task HandleFailureAsync(OutboxRecord outbox, string error, CancellationToken cancellationToken)
    {
        if (outbox.AttemptCount >= Math.Max(1, _settings.OutboxProjectorMaxAttempts))
        {
            await _store.MoveOutboxToDeadLetterAsync(outbox.OutboxId, error, cancellationToken).ConfigureAwait(false);
            DeadLetterLog(_logger, outbox.OutboxId, outbox.AttemptCount, null);
            return;
        }

        var delayMs = ComputeBackoffDelayMs(outbox.AttemptCount);
        var nextAttemptUtc = DateTime.UtcNow.AddMilliseconds(delayMs);
        await _store.ScheduleOutboxRetryAsync(outbox.OutboxId, error, nextAttemptUtc, cancellationToken).ConfigureAwait(false);
        RetryScheduledLog(_logger, outbox.OutboxId, outbox.AttemptCount, nextAttemptUtc.ToString("O"), null);
    }

    private int ComputeBackoffDelayMs(int attemptCount)
    {
        var normalizedAttempt = Math.Max(1, attemptCount);
        var exponent = Math.Min(16, normalizedAttempt - 1);
        var multiplier = 1L << exponent;
        var delay = Math.Max(1, _settings.OutboxProjectorBaseBackoffMs) * multiplier;
        return (int)Math.Min(delay, Math.Max(1, _settings.OutboxProjectorMaxBackoffMs));
    }

    private static PromotedMemoryRecord? BuildMemoryProjection(MissionEventRecord record)
    {
        if (!ShouldProject(record.EventType))
        {
            return null;
        }

        return new PromotedMemoryRecord
        {
            MemoryId = $"event:{record.StreamId}:{record.Version}",
            Kind = "mission_event",
            NamespaceId = string.Equals(record.EventType, "VerificationFailed", StringComparison.OrdinalIgnoreCase)
                || string.Equals(record.EventType, "VerificationPassed", StringComparison.OrdinalIgnoreCase)
                || string.Equals(record.EventType, "ApprovalRequested", StringComparison.OrdinalIgnoreCase)
                ? MemoryNamespaces.EvidenceLessons
                : MemoryNamespaces.ProjectRuntimeMemory,
            Title = $"{record.EventType} :: {record.StreamId}",
            Snippet = BuildSnippet(record),
            SourceRef = $"event://{record.StreamId}/{record.Version}",
            DocumentKey = record.DocumentKey,
            EventType = record.EventType,
            RunId = record.StreamId,
            Promoted = true,
            PayloadJson = record.PayloadJson,
            CreatedUtc = record.OccurredUtc
        };
    }

    private static bool ShouldProject(string eventType)
    {
        return string.Equals(eventType, "TaskCompleted", StringComparison.OrdinalIgnoreCase)
            || string.Equals(eventType, "TaskBlocked", StringComparison.OrdinalIgnoreCase)
            || string.Equals(eventType, "TaskCanceled", StringComparison.OrdinalIgnoreCase)
            || string.Equals(eventType, "VerificationFailed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(eventType, "VerificationPassed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(eventType, "ApprovalRequested", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildSnippet(MissionEventRecord record)
    {
        try
        {
            using var document = JsonDocument.Parse(record.PayloadJson);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    var text = property.Value.ToString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return $"{property.Name}: {text}";
                    }
                }
            }
        }
        catch
        {
            // Fallback below.
        }

        return string.IsNullOrWhiteSpace(record.PayloadJson)
            ? record.EventType
            : record.PayloadJson.Length <= 220
                ? record.PayloadJson
                : record.PayloadJson.Substring(0, 220);
    }
}
