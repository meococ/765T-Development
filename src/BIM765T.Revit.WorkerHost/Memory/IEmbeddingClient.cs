using System.Threading;
using System.Threading.Tasks;

namespace BIM765T.Revit.WorkerHost.Memory;

internal interface IEmbeddingClient
{
    Task<EmbeddingVector> EmbedAsync(string text, CancellationToken cancellationToken);
}
