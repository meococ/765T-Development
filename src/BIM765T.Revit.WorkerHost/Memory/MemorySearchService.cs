using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BIM765T.Revit.WorkerHost.Eventing;
using BIM765T.Revit.Contracts.Platform;
using Microsoft.Extensions.Logging;

namespace BIM765T.Revit.WorkerHost.Memory;

internal sealed class MemorySearchService : IMemorySearchService
{
    private static readonly Action<ILogger, string, Exception?> QdrantUpsertFailedLog =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(8100, nameof(MemorySearchService)), "Qdrant upsert failed for memory record {MemoryId}. Record exists in SQLite but not in Qdrant. Reconciliation will retry.");

    private readonly SqliteMissionEventStore _store;
    private readonly ISemanticMemoryClient _semanticClient;
    private readonly ILogger<MemorySearchService>? _logger;

    public MemorySearchService(SqliteMissionEventStore store, ISemanticMemoryClient semanticClient, ILogger<MemorySearchService>? logger = null)
    {
        _store = store;
        _semanticClient = semanticClient;
        _logger = logger;
    }

    public async Task UpsertAsync(PromotedMemoryRecord record, CancellationToken cancellationToken)
    {
        record.NamespaceId = NormalizeNamespace(record);
        await _store.UpsertMemoryAsync(record, cancellationToken).ConfigureAwait(false);
        try
        {
            await _semanticClient.UpsertAsync(record, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Keep SQLite as durable truth even when Qdrant is unavailable.
            // Log for visibility - reconciliation job should pick these up when Qdrant recovers.
            if (_logger != null)
                QdrantUpsertFailedLog(_logger, record.MemoryId, ex);
        }
    }

    public async Task<IReadOnlyList<SemanticMemoryHit>> SearchAsync(string query, string documentKey, int topK, CancellationToken cancellationToken)
    {
        try
        {
            var semanticHits = await _semanticClient.SearchAsync(query, documentKey, topK, cancellationToken).ConfigureAwait(false);
            if (semanticHits.Count > 0)
            {
                return semanticHits;
            }
        }
        catch
        {
            // Fall back to lexical search below.
        }

        var lexicalHits = await _store.SearchMemoryLexicalAsync(query, documentKey, topK, cancellationToken).ConfigureAwait(false);
        return lexicalHits
            .Select((x, index) => new SemanticMemoryHit
            {
                Id = x.MemoryId,
                Kind = x.Kind,
                Namespace = x.NamespaceId,
                Title = x.Title,
                Snippet = x.Snippet,
                SourceRef = x.SourceRef,
                DocumentKey = x.DocumentKey,
                CreatedUtc = x.CreatedUtc,
                Score = Math.Max(0.01d, 1d - index * 0.1d)
            })
            .ToList();
    }

    public async Task<MemoryScopedSearchResponse> SearchScopedAsync(MemoryScopedSearchRequest request, CancellationToken cancellationToken)
    {
        request ??= new MemoryScopedSearchRequest();
        var namespaces = (request.Namespaces ?? new System.Collections.Generic.List<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (namespaces.Count == 0)
        {
            namespaces.AddRange(DefaultNamespaces(request.RetrievalScope));
        }

        var expandedTopK = Math.Max(4, Math.Max(1, request.MaxResults) * Math.Max(2, namespaces.Count * 2));
        IReadOnlyList<SemanticMemoryHit> hits;
        try
        {
            hits = await _semanticClient.SearchAsync(request.Query ?? string.Empty, request.DocumentKey ?? string.Empty, expandedTopK, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            hits = Array.Empty<SemanticMemoryHit>();
        }

        var scoped = hits
            .Select(x => new ScopedMemoryHit
            {
                Namespace = string.IsNullOrWhiteSpace(x.Namespace) ? ResolveNamespace(x) : x.Namespace,
                Id = x.Id,
                Kind = x.Kind,
                Title = x.Title,
                Snippet = x.Snippet,
                SourceRef = x.SourceRef,
                DocumentKey = x.DocumentKey,
                CreatedUtc = x.CreatedUtc,
                Score = x.Score
            })
            .Where(x => namespaces.Contains(x.Namespace, StringComparer.OrdinalIgnoreCase))
            .Take(Math.Max(1, request.MaxResults))
            .ToList();

        if (scoped.Count < Math.Max(1, request.MaxResults))
        {
            var lexicalHits = await _store.SearchMemoryLexicalAsync(
                request.Query ?? string.Empty,
                request.DocumentKey ?? string.Empty,
                expandedTopK,
                namespaces,
                cancellationToken).ConfigureAwait(false);

            foreach (var hit in lexicalHits.Select((x, index) => new ScopedMemoryHit
                     {
                         Namespace = string.IsNullOrWhiteSpace(x.NamespaceId) ? InferNamespaceFromKind(x.Kind) : x.NamespaceId,
                         Id = x.MemoryId,
                         Kind = x.Kind,
                         Title = x.Title,
                         Snippet = x.Snippet,
                         SourceRef = x.SourceRef,
                         DocumentKey = x.DocumentKey,
                         CreatedUtc = x.CreatedUtc,
                         Score = Math.Max(0.01d, 1d - index * 0.1d)
                     }))
            {
                if (!namespaces.Contains(hit.Namespace, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (scoped.Any(existing => string.Equals(existing.Id, hit.Id, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                scoped.Add(hit);
                if (scoped.Count >= Math.Max(1, request.MaxResults))
                {
                    break;
                }
            }
        }

        return new MemoryScopedSearchResponse
        {
            Query = request.Query ?? string.Empty,
            RetrievalScope = request.RetrievalScope ?? RetrievalScopes.WorkflowPath,
            Hits = scoped,
            Summary = scoped.Count == 0
                ? "No scoped memory hits."
                : $"Retrieved {scoped.Count} scoped memory hit(s) across {scoped.Select(x => x.Namespace).Distinct(StringComparer.OrdinalIgnoreCase).Count()} namespace(s)."
        };
    }

    private static string ResolveNamespace(SemanticMemoryHit hit)
    {
        return InferNamespaceFromKind(hit.Kind);
    }

    private static string[] DefaultNamespaces(string retrievalScope)
    {
        if (string.Equals(retrievalScope, RetrievalScopes.QuickPath, StringComparison.OrdinalIgnoreCase))
        {
            return new[] { MemoryNamespaces.AtlasNativeCommands, MemoryNamespaces.AtlasCustomTools, MemoryNamespaces.AtlasCuratedScripts };
        }

        if (string.Equals(retrievalScope, RetrievalScopes.DeliveryPath, StringComparison.OrdinalIgnoreCase))
        {
            return new[] { MemoryNamespaces.ProjectRuntimeMemory, MemoryNamespaces.EvidenceLessons, MemoryNamespaces.PlaybooksPolicies };
        }

        return new[] { MemoryNamespaces.PlaybooksPolicies, MemoryNamespaces.ProjectRuntimeMemory };
    }

    internal static string NormalizeNamespace(PromotedMemoryRecord record)
    {
        if (!string.IsNullOrWhiteSpace(record.NamespaceId))
        {
            return record.NamespaceId.Trim();
        }

        return InferNamespaceFromKind(record.Kind);
    }

    private static string InferNamespaceFromKind(string? kind)
    {
        if (string.Equals(kind, "atlas_native_command", StringComparison.OrdinalIgnoreCase))
        {
            return MemoryNamespaces.AtlasNativeCommands;
        }

        if (string.Equals(kind, "atlas_custom_tool", StringComparison.OrdinalIgnoreCase))
        {
            return MemoryNamespaces.AtlasCustomTools;
        }

        if (string.Equals(kind, "curated_script", StringComparison.OrdinalIgnoreCase))
        {
            return MemoryNamespaces.AtlasCuratedScripts;
        }

        if (string.Equals(kind, "playbook", StringComparison.OrdinalIgnoreCase)
            || string.Equals(kind, "policy", StringComparison.OrdinalIgnoreCase)
            || string.Equals(kind, "project_memory", StringComparison.OrdinalIgnoreCase)
            || string.Equals(kind, "promotion", StringComparison.OrdinalIgnoreCase))
        {
            return MemoryNamespaces.PlaybooksPolicies;
        }

        if (string.Equals(kind, "lesson", StringComparison.OrdinalIgnoreCase)
            || string.Equals(kind, "evidence_bundle", StringComparison.OrdinalIgnoreCase))
        {
            return MemoryNamespaces.EvidenceLessons;
        }

        return MemoryNamespaces.ProjectRuntimeMemory;
    }
}
