using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BIM765T.Revit.WorkerHost.Eventing;

namespace BIM765T.Revit.WorkerHost.Memory;

internal interface ISemanticMemoryClient
{
    Task EnsureReadyAsync(CancellationToken cancellationToken);

    Task UpsertAsync(PromotedMemoryRecord record, CancellationToken cancellationToken);

    Task<IReadOnlyList<SemanticMemoryHit>> SearchAsync(string query, string documentKey, int topK, CancellationToken cancellationToken);
}
