using System.Threading;
using System.Threading.Tasks;

namespace BIM765T.Revit.WorkerHost.Memory;

internal interface IEmbeddingProvider
{
    string ProviderId { get; }

    bool IsSemantic { get; }

    Task<EmbeddingVector> EmbedAsync(string text, CancellationToken cancellationToken);
}
