using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace BIM765T.Revit.WorkerHost.Eventing;

internal sealed class SqliteMissionEventStore
{
    private readonly string _connectionString;

    public SqliteMissionEventStore(string databasePath)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();

        EnsureCreated();
    }

    public async Task<long> AppendAsync(MissionEventRecord record, string snapshotJson, CancellationToken cancellationToken)
    {
        return await AppendBatchAsync(new[] { record }, snapshotJson, cancellationToken).ConfigureAwait(false);
    }

    public async Task<long> AppendBatchAsync(IReadOnlyList<MissionEventRecord> records, string snapshotJson, CancellationToken cancellationToken)
    {
        if (records == null || records.Count == 0)
        {
            throw new ArgumentException("At least one event record is required.", nameof(records));
        }

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await ConfigureAsync(connection, cancellationToken).ConfigureAwait(false);
        using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var streamId = records[0].StreamId;
        long nextVersion;
        await using (var versionCommand = connection.CreateCommand())
        {
            versionCommand.Transaction = transaction;
            versionCommand.CommandText = "SELECT COALESCE(MAX(version), 0) FROM events WHERE stream_id = $streamId;";
            versionCommand.Parameters.AddWithValue("$streamId", streamId);
            nextVersion = Convert.ToInt64(await versionCommand.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture) + 1L;
        }

        foreach (var record in records)
        {
            if (!string.Equals(record.StreamId, streamId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("AppendBatchAsync only supports one stream per batch.");
            }

            record.Version = nextVersion++;
            await using var insertCommand = connection.CreateCommand();
            insertCommand.Transaction = transaction;
            insertCommand.CommandText =
                """
                INSERT INTO events(stream_id, version, event_type, payload_blob, occurred_utc, correlation_id, causation_id, actor_id, document_key, terminal)
                VALUES($streamId, $version, $eventType, $payload, $occurredUtc, $correlationId, $causationId, $actorId, $documentKey, $terminal);
                """;
            insertCommand.Parameters.AddWithValue("$streamId", record.StreamId);
            insertCommand.Parameters.AddWithValue("$version", record.Version);
            insertCommand.Parameters.AddWithValue("$eventType", record.EventType);
            insertCommand.Parameters.AddWithValue("$payload", record.PayloadJson);
            insertCommand.Parameters.AddWithValue("$occurredUtc", record.OccurredUtc);
            insertCommand.Parameters.AddWithValue("$correlationId", record.CorrelationId);
            insertCommand.Parameters.AddWithValue("$causationId", record.CausationId);
            insertCommand.Parameters.AddWithValue("$actorId", record.ActorId);
            insertCommand.Parameters.AddWithValue("$documentKey", record.DocumentKey);
            insertCommand.Parameters.AddWithValue("$terminal", record.Terminal ? 1 : 0);
            await insertCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        var lastRecord = records[^1];

        // Insert outbox entries BEFORE snapshot to ensure events are always recoverable
        // even if crash happens after snapshot commit but before outbox completes
        foreach (var record in records)
        {
            await using var outboxCommand = connection.CreateCommand();
            outboxCommand.Transaction = transaction;
            outboxCommand.CommandText =
                """
                INSERT INTO outbox(event_stream_id, event_version, target, status, attempt_count, leased_utc, next_attempt_utc, last_error, last_attempt_utc)
                VALUES($streamId, $version, $target, 'pending', 0, '', '', '', '');
                """;
            outboxCommand.Parameters.AddWithValue("$streamId", record.StreamId);
            outboxCommand.Parameters.AddWithValue("$version", record.Version);
            outboxCommand.Parameters.AddWithValue("$target", "memory");
            await outboxCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var snapshotCommand = connection.CreateCommand())
        {
            snapshotCommand.Transaction = transaction;
            snapshotCommand.CommandText =
                """
                INSERT INTO snapshots(stream_id, version, payload_blob)
                VALUES($streamId, $version, $payload)
                ON CONFLICT(stream_id)
                DO UPDATE SET version = excluded.version, payload_blob = excluded.payload_blob;
                """;
            snapshotCommand.Parameters.AddWithValue("$streamId", streamId);
            snapshotCommand.Parameters.AddWithValue("$version", lastRecord.Version);
            snapshotCommand.Parameters.AddWithValue("$payload", snapshotJson);
            await snapshotCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return lastRecord.Version;
    }

    public async Task<StoreStatistics> GetStatisticsAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await ConfigureAsync(connection, cancellationToken).ConfigureAwait(false);

        return new StoreStatistics
        {
            EventCount = await ExecuteCountAsync(connection, "SELECT COUNT(*) FROM events;", cancellationToken).ConfigureAwait(false),
            SnapshotCount = await ExecuteCountAsync(connection, "SELECT COUNT(*) FROM snapshots;", cancellationToken).ConfigureAwait(false),
            PendingOutboxCount = await ExecuteCountAsync(connection, "SELECT COUNT(*) FROM outbox WHERE status = 'pending';", cancellationToken).ConfigureAwait(false),
            ProcessingOutboxCount = await ExecuteCountAsync(connection, "SELECT COUNT(*) FROM outbox WHERE status = 'processing';", cancellationToken).ConfigureAwait(false),
            CompletedOutboxCount = await ExecuteCountAsync(connection, "SELECT COUNT(*) FROM outbox WHERE status = 'completed';", cancellationToken).ConfigureAwait(false),
            FailedOutboxCount = await ExecuteCountAsync(connection, "SELECT COUNT(*) FROM outbox WHERE status IN ('failed', 'dead_letter');", cancellationToken).ConfigureAwait(false),
            DeadLetterOutboxCount = await ExecuteCountAsync(connection, "SELECT COUNT(*) FROM outbox WHERE status = 'dead_letter';", cancellationToken).ConfigureAwait(false),
            IgnoredOutboxCount = await ExecuteCountAsync(connection, "SELECT COUNT(*) FROM outbox WHERE status = 'ignored';", cancellationToken).ConfigureAwait(false),
            BackoffPendingOutboxCount = await ExecuteCountAsync(connection, "SELECT COUNT(*) FROM outbox WHERE status = 'pending' AND next_attempt_utc <> '';", cancellationToken).ConfigureAwait(false),
            MemoryProjectionCount = await ExecuteCountAsync(connection, "SELECT COUNT(*) FROM memory_projection;", cancellationToken).ConfigureAwait(false),
            MigrationCount = await ExecuteCountAsync(connection, "SELECT COUNT(*) FROM migration_journal;", cancellationToken).ConfigureAwait(false)
        };
    }

    public async Task<bool> TryRegisterMigrationAsync(string sourceKind, string sourceId, string sourcePath, string status, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await ConfigureAsync(connection, cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT OR IGNORE INTO migration_journal(source_kind, source_id, source_path, imported_utc, status)
            VALUES($sourceKind, $sourceId, $sourcePath, $importedUtc, $status);
            """;
        command.Parameters.AddWithValue("$sourceKind", sourceKind);
        command.Parameters.AddWithValue("$sourceId", sourceId);
        command.Parameters.AddWithValue("$sourcePath", sourcePath);
        command.Parameters.AddWithValue("$importedUtc", UtcNowString());
        command.Parameters.AddWithValue("$status", status);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;
    }

    public async Task<List<MissionEventRecord>> ListAsync(string streamId, CancellationToken cancellationToken)
    {
        var records = new List<MissionEventRecord>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await ConfigureAsync(connection, cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT stream_id, version, event_type, payload_blob, occurred_utc, correlation_id, causation_id, actor_id, document_key, terminal
            FROM events
            WHERE stream_id = $streamId
            ORDER BY version ASC;
            """;
        command.Parameters.AddWithValue("$streamId", streamId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            records.Add(new MissionEventRecord
            {
                StreamId = reader.GetString(0),
                Version = reader.GetInt64(1),
                EventType = reader.GetString(2),
                PayloadJson = reader.GetString(3),
                OccurredUtc = reader.GetString(4),
                CorrelationId = reader.GetString(5),
                CausationId = reader.GetString(6),
                ActorId = reader.GetString(7),
                DocumentKey = reader.GetString(8),
                Terminal = reader.GetInt32(9) == 1
            });
        }

        return records;
    }

    public async Task CheckpointAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await ConfigureAsync(connection, cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA wal_checkpoint(PASSIVE);";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<MissionSnapshot?> TryGetSnapshotAsync(string streamId, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await ConfigureAsync(connection, cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload_blob FROM snapshots WHERE stream_id = $streamId;";
        command.Parameters.AddWithValue("$streamId", streamId);
        var payload = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
        if (!string.IsNullOrWhiteSpace(payload))
        {
            try
            {
                return System.Text.Json.JsonSerializer.Deserialize<MissionSnapshot>(payload);
            }
            catch
            {
                // Fall back to replay below.
            }
        }

        var records = await ListAsync(streamId, cancellationToken).ConfigureAwait(false);
        var replayed = MissionSnapshotReplayer.Replay(streamId, records);
        if (replayed != null)
        {
            await UpsertSnapshotAsync(replayed, cancellationToken).ConfigureAwait(false);
        }

        return replayed;
    }

    public async Task DeleteSnapshotAsync(string streamId, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await ConfigureAsync(connection, cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM snapshots WHERE stream_id = $streamId;";
        command.Parameters.AddWithValue("$streamId", streamId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<MissionEventRecord?> TryGetEventAsync(string streamId, long version, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await ConfigureAsync(connection, cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT stream_id, version, event_type, payload_blob, occurred_utc, correlation_id, causation_id, actor_id, document_key, terminal
            FROM events
            WHERE stream_id = $streamId AND version = $version;
            """;
        command.Parameters.AddWithValue("$streamId", streamId);
        command.Parameters.AddWithValue("$version", version);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new MissionEventRecord
        {
            StreamId = reader.GetString(0),
            Version = reader.GetInt64(1),
            EventType = reader.GetString(2),
            PayloadJson = reader.GetString(3),
            OccurredUtc = reader.GetString(4),
            CorrelationId = reader.GetString(5),
            CausationId = reader.GetString(6),
            ActorId = reader.GetString(7),
            DocumentKey = reader.GetString(8),
            Terminal = reader.GetInt32(9) == 1
        };
    }

    public async Task<OutboxRecord?> TryGetOutboxAsync(string streamId, long eventVersion, string target, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await ConfigureAsync(connection, cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT outbox_id, event_stream_id, event_version, target, status, attempt_count, leased_utc, last_attempt_utc, next_attempt_utc, last_error
            FROM outbox
            WHERE event_stream_id = $streamId AND event_version = $eventVersion AND target = $target
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$streamId", streamId);
        command.Parameters.AddWithValue("$eventVersion", eventVersion);
        command.Parameters.AddWithValue("$target", target);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? MapOutboxRecord(reader)
            : null;
    }

    public async Task<int> RequeueStaleProcessingAsync(string target, TimeSpan staleAfter, CancellationToken cancellationToken)
    {
        if (staleAfter <= TimeSpan.Zero)
        {
            return 0;
        }

        var cutoffUtc = DateTime.UtcNow.Subtract(staleAfter).ToString("O", CultureInfo.InvariantCulture);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await ConfigureAsync(connection, cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE outbox
            SET status = 'pending',
                leased_utc = '',
                next_attempt_utc = '',
                last_error = CASE
                    WHEN last_error = '' THEN 'Outbox lease expired and was re-queued.'
                    ELSE last_error
                END
            WHERE target = $target
              AND status = 'processing'
              AND leased_utc <> ''
              AND leased_utc <= $cutoffUtc;
            """;
        command.Parameters.AddWithValue("$target", target);
        command.Parameters.AddWithValue("$cutoffUtc", cutoffUtc);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<List<OutboxRecord>> LeaseOutboxBatchAsync(string target, int maxCount, CancellationToken cancellationToken)
    {
        var leased = new List<OutboxRecord>();
        var candidates = new List<OutboxRecord>();
        var nowUtc = UtcNowString();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await ConfigureAsync(connection, cancellationToken).ConfigureAwait(false);

        await using var select = connection.CreateCommand();
        select.CommandText =
            """
            SELECT outbox_id, event_stream_id, event_version, target, status, attempt_count, leased_utc, last_attempt_utc, next_attempt_utc, last_error
            FROM outbox
            WHERE target = $target
              AND status = 'pending'
              AND (next_attempt_utc = '' OR next_attempt_utc <= $nowUtc)
            ORDER BY outbox_id ASC
            LIMIT $limit;
            """;
        select.Parameters.AddWithValue("$target", target);
        select.Parameters.AddWithValue("$nowUtc", nowUtc);
        select.Parameters.AddWithValue("$limit", Math.Max(1, maxCount));

        await using var reader = await select.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            candidates.Add(MapOutboxRecord(reader));
        }

        foreach (var candidate in candidates)
        {
            if (await TryLeaseOutboxAsync(candidate.OutboxId, nowUtc, cancellationToken).ConfigureAwait(false))
            {
                candidate.Status = "processing";
                candidate.AttemptCount += 1;
                candidate.LeasedUtc = nowUtc;
                candidate.LastAttemptUtc = nowUtc;
                candidate.NextAttemptUtc = string.Empty;
                leased.Add(candidate);
            }
        }

        return leased;
    }

    public async Task ScheduleOutboxRetryAsync(long outboxId, string error, DateTime nextAttemptUtc, CancellationToken cancellationToken, string expectedStatus = "processing")
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await ConfigureAsync(connection, cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE outbox
            SET status = 'pending',
                leased_utc = '',
                next_attempt_utc = $nextAttemptUtc,
                last_error = $lastError
            WHERE outbox_id = $outboxId
              AND ($expectedStatus = '' OR status = $expectedStatus);
            """;
        command.Parameters.AddWithValue("$outboxId", outboxId);
        command.Parameters.AddWithValue("$nextAttemptUtc", nextAttemptUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$lastError", error ?? string.Empty);
        command.Parameters.AddWithValue("$expectedStatus", expectedStatus ?? string.Empty);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task CompleteOutboxAsync(long outboxId, string finalStatus, CancellationToken cancellationToken, string expectedStatus = "processing")
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await ConfigureAsync(connection, cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE outbox
            SET status = $status,
                leased_utc = '',
                next_attempt_utc = '',
                last_error = CASE WHEN $status = 'completed' OR $status = 'ignored' THEN '' ELSE last_error END
            WHERE outbox_id = $outboxId
              AND ($expectedStatus = '' OR status = $expectedStatus);
            """;
        command.Parameters.AddWithValue("$status", finalStatus);
        command.Parameters.AddWithValue("$outboxId", outboxId);
        command.Parameters.AddWithValue("$expectedStatus", expectedStatus ?? string.Empty);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task MoveOutboxToDeadLetterAsync(long outboxId, string error, CancellationToken cancellationToken, string expectedStatus = "processing")
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await ConfigureAsync(connection, cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE outbox
            SET status = 'dead_letter',
                leased_utc = '',
                next_attempt_utc = '',
                last_error = $lastError
            WHERE outbox_id = $outboxId
              AND ($expectedStatus = '' OR status = $expectedStatus);
            """;
        command.Parameters.AddWithValue("$outboxId", outboxId);
        command.Parameters.AddWithValue("$lastError", error ?? string.Empty);
        command.Parameters.AddWithValue("$expectedStatus", expectedStatus ?? string.Empty);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> UpdateOutboxStatusAsync(long outboxId, string newStatus, CancellationToken cancellationToken, string expectedStatus = "")
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await ConfigureAsync(connection, cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = string.IsNullOrWhiteSpace(expectedStatus)
            ? "UPDATE outbox SET status = $status WHERE outbox_id = $outboxId;"
            : "UPDATE outbox SET status = $status WHERE outbox_id = $outboxId AND status = $expectedStatus;";
        command.Parameters.AddWithValue("$status", newStatus);
        command.Parameters.AddWithValue("$outboxId", outboxId);
        if (!string.IsNullOrWhiteSpace(expectedStatus))
        {
            command.Parameters.AddWithValue("$expectedStatus", expectedStatus);
        }

        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;
    }

    public async Task UpsertMemoryAsync(PromotedMemoryRecord record, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await ConfigureAsync(connection, cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO memory_projection(memory_id, namespace_id, kind, title, snippet, source_ref, document_key, event_type, run_id, promoted, payload_blob, created_utc)
            VALUES($memoryId, $namespaceId, $kind, $title, $snippet, $sourceRef, $documentKey, $eventType, $runId, $promoted, $payload, $createdUtc)
            ON CONFLICT(memory_id)
            DO UPDATE SET
                namespace_id = excluded.namespace_id,
                kind = excluded.kind,
                title = excluded.title,
                snippet = excluded.snippet,
                source_ref = excluded.source_ref,
                document_key = excluded.document_key,
                event_type = excluded.event_type,
                run_id = excluded.run_id,
                promoted = excluded.promoted,
                payload_blob = excluded.payload_blob,
                created_utc = excluded.created_utc;
            """;
        command.Parameters.AddWithValue("$memoryId", record.MemoryId);
        command.Parameters.AddWithValue("$namespaceId", record.NamespaceId ?? string.Empty);
        command.Parameters.AddWithValue("$kind", record.Kind);
        command.Parameters.AddWithValue("$title", record.Title);
        command.Parameters.AddWithValue("$snippet", record.Snippet);
        command.Parameters.AddWithValue("$sourceRef", record.SourceRef);
        command.Parameters.AddWithValue("$documentKey", record.DocumentKey);
        command.Parameters.AddWithValue("$eventType", record.EventType);
        command.Parameters.AddWithValue("$runId", record.RunId);
        command.Parameters.AddWithValue("$promoted", record.Promoted ? 1 : 0);
        command.Parameters.AddWithValue("$payload", record.PayloadJson);
        command.Parameters.AddWithValue("$createdUtc", record.CreatedUtc);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<List<PromotedMemoryRecord>> SearchMemoryLexicalAsync(string query, string documentKey, int topK, CancellationToken cancellationToken)
    {
        return SearchMemoryLexicalAsync(query, documentKey, topK, namespaces: null, cancellationToken);
    }

    public async Task<List<PromotedMemoryRecord>> SearchMemoryLexicalAsync(string query, string documentKey, int topK, IReadOnlyCollection<string>? namespaces, CancellationToken cancellationToken)
    {
        var records = new List<PromotedMemoryRecord>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await ConfigureAsync(connection, cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT memory_id, namespace_id, kind, title, snippet, source_ref, document_key, event_type, run_id, promoted, payload_blob, created_utc
            FROM memory_projection
            WHERE promoted = 1
              AND ($documentKey = '' OR document_key = $documentKey OR document_key = '')
              AND ($namespaceFilter = 0 OR namespace_id IN (SELECT value FROM json_each($namespacesJson)))
              AND (title LIKE $term OR snippet LIKE $term OR payload_blob LIKE $term)
            ORDER BY created_utc DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$documentKey", documentKey ?? string.Empty);
        command.Parameters.AddWithValue("$namespaceFilter", namespaces != null && namespaces.Count > 0 ? 1 : 0);
        command.Parameters.AddWithValue("$namespacesJson", namespaces != null && namespaces.Count > 0 ? System.Text.Json.JsonSerializer.Serialize(namespaces) : "[]");
        command.Parameters.AddWithValue("$term", "%" + (query ?? string.Empty) + "%");
        command.Parameters.AddWithValue("$limit", Math.Max(1, topK));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            records.Add(new PromotedMemoryRecord
            {
                MemoryId = reader.GetString(0),
                NamespaceId = reader.GetString(1),
                Kind = reader.GetString(2),
                Title = reader.GetString(3),
                Snippet = reader.GetString(4),
                SourceRef = reader.GetString(5),
                DocumentKey = reader.GetString(6),
                EventType = reader.GetString(7),
                RunId = reader.GetString(8),
                Promoted = reader.GetInt32(9) == 1,
                PayloadJson = reader.GetString(10),
                CreatedUtc = reader.GetString(11)
            });
        }

        return records;
    }

    private void EnsureCreated()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            PRAGMA journal_mode=WAL;
            PRAGMA synchronous=FULL;
            PRAGMA foreign_keys=ON;
            PRAGMA busy_timeout=5000;

            CREATE TABLE IF NOT EXISTS events (
                stream_id TEXT NOT NULL,
                version INTEGER NOT NULL,
                event_type TEXT NOT NULL,
                payload_blob TEXT NOT NULL,
                occurred_utc TEXT NOT NULL,
                correlation_id TEXT NOT NULL,
                causation_id TEXT NOT NULL,
                actor_id TEXT NOT NULL,
                document_key TEXT NOT NULL,
                terminal INTEGER NOT NULL DEFAULT 0,
                PRIMARY KEY(stream_id, version)
            );

            CREATE TABLE IF NOT EXISTS snapshots (
                stream_id TEXT NOT NULL PRIMARY KEY,
                version INTEGER NOT NULL,
                payload_blob TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS outbox (
                outbox_id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                event_stream_id TEXT NOT NULL,
                event_version INTEGER NOT NULL,
                target TEXT NOT NULL,
                status TEXT NOT NULL,
                attempt_count INTEGER NOT NULL DEFAULT 0,
                leased_utc TEXT NOT NULL DEFAULT '',
                next_attempt_utc TEXT NOT NULL DEFAULT '',
                last_error TEXT NOT NULL DEFAULT '',
                last_attempt_utc TEXT NOT NULL DEFAULT ''
            );

            CREATE TABLE IF NOT EXISTS memory_projection (
                memory_id TEXT NOT NULL PRIMARY KEY,
                namespace_id TEXT NOT NULL DEFAULT '',
                kind TEXT NOT NULL,
                title TEXT NOT NULL,
                snippet TEXT NOT NULL,
                source_ref TEXT NOT NULL,
                document_key TEXT NOT NULL,
                event_type TEXT NOT NULL,
                run_id TEXT NOT NULL,
                promoted INTEGER NOT NULL DEFAULT 1,
                payload_blob TEXT NOT NULL,
                created_utc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS migration_journal (
                source_kind TEXT NOT NULL,
                source_id TEXT NOT NULL,
                source_path TEXT NOT NULL,
                imported_utc TEXT NOT NULL,
                status TEXT NOT NULL,
                PRIMARY KEY(source_kind, source_id)
            );
            """;
        command.ExecuteNonQuery();

        EnsureColumnExists(connection, "outbox", "attempt_count", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumnExists(connection, "outbox", "leased_utc", "TEXT NOT NULL DEFAULT ''");
        EnsureColumnExists(connection, "outbox", "next_attempt_utc", "TEXT NOT NULL DEFAULT ''");
        EnsureColumnExists(connection, "outbox", "last_error", "TEXT NOT NULL DEFAULT ''");
        EnsureColumnExists(connection, "outbox", "last_attempt_utc", "TEXT NOT NULL DEFAULT ''");
        EnsureColumnExists(connection, "memory_projection", "namespace_id", "TEXT NOT NULL DEFAULT ''");
        EnsureIndexExists(connection, "CREATE INDEX IF NOT EXISTS idx_outbox_target_status_next_attempt_utc ON outbox(target, status, next_attempt_utc, outbox_id);");
        EnsureIndexExists(connection, "CREATE INDEX IF NOT EXISTS idx_memory_projection_namespace_document_created ON memory_projection(namespace_id, document_key, created_utc DESC);");
    }

    private static async Task ConfigureAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            PRAGMA journal_mode=WAL;
            PRAGMA synchronous=FULL;
            PRAGMA foreign_keys=ON;
            PRAGMA busy_timeout=5000;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<long> ExecuteCountAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
    }

    private static OutboxRecord MapOutboxRecord(SqliteDataReader reader)
    {
        return new OutboxRecord
        {
            OutboxId = reader.GetInt64(0),
            StreamId = reader.GetString(1),
            EventVersion = reader.GetInt64(2),
            Target = reader.GetString(3),
            Status = reader.GetString(4),
            AttemptCount = reader.GetInt32(5),
            LeasedUtc = reader.GetString(6),
            LastAttemptUtc = reader.GetString(7),
            NextAttemptUtc = reader.GetString(8),
            LastError = reader.GetString(9)
        };
    }

    private static void EnsureColumnExists(SqliteConnection connection, string tableName, string columnName, string definition)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(1) FROM pragma_table_info('{tableName}') WHERE name = $columnName;";
        command.Parameters.AddWithValue("$columnName", columnName);
        var exists = Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
        if (exists)
        {
            return;
        }

        using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {definition};";
        alter.ExecuteNonQuery();
    }

    private static void EnsureIndexExists(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static string UtcNowString()
    {
        return DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
    }

    private async Task<bool> TryLeaseOutboxAsync(long outboxId, string nowUtc, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await ConfigureAsync(connection, cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE outbox
            SET status = 'processing',
                attempt_count = attempt_count + 1,
                leased_utc = $nowUtc,
                last_attempt_utc = $nowUtc,
                next_attempt_utc = ''
            WHERE outbox_id = $outboxId
              AND status = 'pending'
              AND (next_attempt_utc = '' OR next_attempt_utc <= $nowUtc);
            """;
        command.Parameters.AddWithValue("$outboxId", outboxId);
        command.Parameters.AddWithValue("$nowUtc", nowUtc);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;
    }

    private async Task UpsertSnapshotAsync(MissionSnapshot snapshot, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await ConfigureAsync(connection, cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO snapshots(stream_id, version, payload_blob)
            VALUES($streamId, $version, $payload)
            ON CONFLICT(stream_id)
            DO UPDATE SET version = excluded.version, payload_blob = excluded.payload_blob;
            """;
        command.Parameters.AddWithValue("$streamId", snapshot.MissionId);
        command.Parameters.AddWithValue("$version", snapshot.Version);
        command.Parameters.AddWithValue("$payload", System.Text.Json.JsonSerializer.Serialize(snapshot));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Prunes old outbox entries that are completed, ignored, or dead-lettered.
    /// Keeps entries newer than retentionDays and at most maxEntriesPerStatus old entries for each terminal status category.
    /// </summary>
    public async Task<int> PruneOutboxAsync(int retentionDays, int maxEntriesPerStatus, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        var effectiveRetentionDays = Math.Max(0, retentionDays);
        var effectiveMaxEntriesPerStatus = Math.Max(0, maxEntriesPerStatus);
        var cutoffUtc = DateTime.UtcNow.AddDays(-effectiveRetentionDays).ToString("O", CultureInfo.InvariantCulture);

        // Delete old completed/ignored/dead_letter entries beyond per-status retention.
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            WITH ranked AS (
                SELECT outbox_id,
                       ROW_NUMBER() OVER (PARTITION BY status ORDER BY last_attempt_utc DESC) as rn
                FROM outbox
                WHERE status IN ('completed', 'ignored', 'dead_letter')
                  AND last_attempt_utc <> ''
                  AND last_attempt_utc < $cutoffUtc
            )
            DELETE FROM outbox WHERE outbox_id IN (SELECT outbox_id FROM ranked WHERE rn > $maxEntriesPerStatus);
            """;
        command.Parameters.AddWithValue("$cutoffUtc", cutoffUtc);
        command.Parameters.AddWithValue("$maxEntriesPerStatus", effectiveMaxEntriesPerStatus);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
