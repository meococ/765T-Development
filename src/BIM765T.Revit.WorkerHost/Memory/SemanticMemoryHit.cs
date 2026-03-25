using System.Collections.Generic;

namespace BIM765T.Revit.WorkerHost.Memory;

internal sealed class SemanticMemoryHit
{
    public string Id { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;

    public string Namespace { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Snippet { get; set; } = string.Empty;

    public string SourceRef { get; set; } = string.Empty;

    public string DocumentKey { get; set; } = string.Empty;

    public string CreatedUtc { get; set; } = string.Empty;

    public double Score { get; set; }

    public List<string> Tags { get; set; } = new List<string>();
}
