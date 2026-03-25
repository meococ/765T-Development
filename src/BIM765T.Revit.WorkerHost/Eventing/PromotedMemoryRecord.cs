namespace BIM765T.Revit.WorkerHost.Eventing;

internal sealed class PromotedMemoryRecord
{
    public string MemoryId { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;

    public string NamespaceId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Snippet { get; set; } = string.Empty;

    public string SourceRef { get; set; } = string.Empty;

    public string DocumentKey { get; set; } = string.Empty;

    public string EventType { get; set; } = string.Empty;

    public string RunId { get; set; } = string.Empty;

    public bool Promoted { get; set; } = true;

    public string PayloadJson { get; set; } = "{}";

    public string CreatedUtc { get; set; } = string.Empty;
}
