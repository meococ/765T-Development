using System.Collections.Generic;
using BIM765T.Revit.WorkerHost.Memory;

namespace BIM765T.Revit.WorkerHost.Routing;

internal sealed class RetrievalContext
{
    public List<SemanticMemoryHit> Hits { get; set; } = new List<SemanticMemoryHit>();

    public string Summary { get; set; } = string.Empty;
}
