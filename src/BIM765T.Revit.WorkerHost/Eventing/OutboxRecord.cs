namespace BIM765T.Revit.WorkerHost.Eventing;

internal sealed class OutboxRecord
{
    public long OutboxId { get; set; }

    public string StreamId { get; set; } = string.Empty;

    public long EventVersion { get; set; }

    public string Target { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public int AttemptCount { get; set; }

    public string LeasedUtc { get; set; } = string.Empty;

    public string LastAttemptUtc { get; set; } = string.Empty;

    public string NextAttemptUtc { get; set; } = string.Empty;

    public string LastError { get; set; } = string.Empty;
}
