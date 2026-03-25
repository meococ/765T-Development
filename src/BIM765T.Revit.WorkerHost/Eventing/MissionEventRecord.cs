namespace BIM765T.Revit.WorkerHost.Eventing;

internal sealed class MissionEventRecord
{
    public string StreamId { get; set; } = string.Empty;

    public long Version { get; set; }

    public string EventType { get; set; } = string.Empty;

    public string PayloadJson { get; set; } = "{}";

    public string OccurredUtc { get; set; } = string.Empty;

    public string CorrelationId { get; set; } = string.Empty;

    public string CausationId { get; set; } = string.Empty;

    public string ActorId { get; set; } = string.Empty;

    public string DocumentKey { get; set; } = string.Empty;

    public bool Terminal { get; set; }
}
