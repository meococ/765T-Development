using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using BIM765T.Revit.Agent.Infrastructure.Logging;
using BIM765T.Revit.Agent.Infrastructure.Time;
using BIM765T.Revit.Contracts.Platform;
using BIM765T.Revit.Contracts.Serialization;

namespace BIM765T.Revit.Agent.Infrastructure.Observability;

/// <summary>
/// Ghi audit trail cho mọi tool call.
///
/// P1-3 FIX:
/// - Honor EnableOperationJournal setting (có thể tắt file persistence)
/// - In-memory ring buffer LUÔN hoạt động (cho ActivityTab + session queries)
/// - File append chạy trên background thread (không block UI thread)
/// - Thread-safe qua ConcurrentQueue + ThreadPool
/// </summary>
internal sealed class OperationJournalService
{
    private readonly ConcurrentQueue<OperationJournalEntry> _recent = new ConcurrentQueue<OperationJournalEntry>();
    private readonly ConcurrentQueue<OperationJournalEntry> _pendingFileEntries = new ConcurrentQueue<OperationJournalEntry>();
    private readonly int _maxItems;
    private readonly string _journalDirectory;
    private readonly IAgentLogger _logger;
    private readonly bool _persistToFile;
    private readonly ISystemClock _clock;
    private int _fileDrainScheduled;

    internal OperationJournalService(IAgentLogger logger, int maxItems, bool persistToFile, ISystemClock clock)
    {
        _logger = logger;
        _maxItems = Math.Max(20, maxItems);
        _persistToFile = persistToFile;
        _clock = clock;
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _journalDirectory = Path.Combine(appData, BIM765T.Revit.Contracts.Common.BridgeConstants.AppDataFolderName, "journal");
        if (_persistToFile)
        {
            Directory.CreateDirectory(_journalDirectory);
        }
    }

    internal void Record(OperationJournalEntry entry)
    {
        // In-memory ring buffer — luôn hoạt động cho session queries + ActivityTab
        _recent.Enqueue(entry);
        var trimAttempts = 0;
        while (_recent.Count > _maxItems && trimAttempts++ < _maxItems && _recent.TryDequeue(out _))
        {
        }

        // P1-3 FIX: Honor EnableOperationJournal toggle
        if (!_persistToFile)
        {
            return;
        }

        // P1-3 FIX: File append trên background thread → không block UI thread
        _pendingFileEntries.Enqueue(entry);
        ScheduleFileDrain();
    }

    internal List<OperationJournalEntry> GetRecent()
    {
        return new List<OperationJournalEntry>(_recent.ToArray());
    }

    private void ScheduleFileDrain()
    {
        if (Interlocked.CompareExchange(ref _fileDrainScheduled, 1, 0) != 0)
        {
            return;
        }

        ThreadPool.QueueUserWorkItem(_ => DrainPendingFileEntries());
    }

    private void DrainPendingFileEntries()
    {
        try
        {
            var path = Path.Combine(_journalDirectory, $"{_clock.Now:yyyyMMdd}.jsonl");
            using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            using var writer = new StreamWriter(stream);
            while (_pendingFileEntries.TryDequeue(out var entry))
            {
                writer.WriteLine(JsonUtil.Serialize(entry));
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to write journal entry to file.", ex);
        }
        finally
        {
            Interlocked.Exchange(ref _fileDrainScheduled, 0);
            if (!_pendingFileEntries.IsEmpty)
            {
                ScheduleFileDrain();
            }
        }
    }
}
