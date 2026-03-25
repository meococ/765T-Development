namespace BIM765T.Revit.WorkerHost.Eventing;

internal sealed class StoreStatistics
{
    public long EventCount { get; set; }

    public long SnapshotCount { get; set; }

    public long PendingOutboxCount { get; set; }

    public long ProcessingOutboxCount { get; set; }

    public long CompletedOutboxCount { get; set; }

    public long FailedOutboxCount { get; set; }

    public long DeadLetterOutboxCount { get; set; }

    public long IgnoredOutboxCount { get; set; }

    public long BackoffPendingOutboxCount { get; set; }

    public long MemoryProjectionCount { get; set; }

    public long MigrationCount { get; set; }
}
