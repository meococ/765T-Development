using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BIM765T.Revit.WorkerHost.Memory;

namespace BIM765T.Revit.WorkerHost.Routing;

internal sealed class RetrieverAgent
{
    private readonly IMemorySearchService _memorySearch;

    public RetrieverAgent(IMemorySearchService memorySearch)
    {
        _memorySearch = memorySearch;
    }

    public async Task<RetrievalContext> RetrieveAsync(string query, string documentKey, int topK, CancellationToken cancellationToken)
    {
        var hits = await _memorySearch.SearchAsync(query ?? string.Empty, documentKey ?? string.Empty, topK, cancellationToken).ConfigureAwait(false);
        return new RetrievalContext
        {
            Hits = hits.ToList(),
            Summary = hits.Count == 0
                ? "No promoted memory hits."
                : $"Retrieved {hits.Count} promoted memory hit(s)."
        };
    }
}
