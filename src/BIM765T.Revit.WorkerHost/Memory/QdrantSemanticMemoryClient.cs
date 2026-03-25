using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BIM765T.Revit.WorkerHost.Configuration;
using BIM765T.Revit.WorkerHost.Eventing;

namespace BIM765T.Revit.WorkerHost.Memory;

internal sealed class QdrantSemanticMemoryClient : ISemanticMemoryClient
{
    private readonly HttpClient _httpClient;
    private readonly WorkerHostSettings _settings;
    private readonly IEmbeddingProvider _embeddingProvider;

    public QdrantSemanticMemoryClient(HttpClient httpClient, WorkerHostSettings settings, IEmbeddingProvider embeddingProvider)
    {
        _httpClient = httpClient;
        _settings = settings;
        _embeddingProvider = embeddingProvider;
    }

    public async Task EnsureReadyAsync(CancellationToken cancellationToken)
    {
        foreach (var namespaceId in _settings.GetSemanticNamespaces())
        {
            var payload = JsonSerializer.Serialize(new
            {
                vectors = new
                {
                    size = _settings.EmbeddingDimensions,
                    distance = "Cosine"
                }
            });

            using var request = new HttpRequestMessage(HttpMethod.Put, $"collections/{_settings.ResolveQdrantCollectionName(namespaceId)}")
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            _ = response.IsSuccessStatusCode;
        }
    }

    public async Task UpsertAsync(PromotedMemoryRecord record, CancellationToken cancellationToken)
    {
        var vector = await _embeddingProvider.EmbedAsync(record.Title + "\n" + record.Snippet + "\n" + record.PayloadJson, cancellationToken).ConfigureAwait(false);
        var payload = JsonSerializer.Serialize(new
        {
            points = new[]
            {
                new
                {
                    id = record.MemoryId,
                    vector = vector.Values,
                    payload = new
                    {
                        kind = record.Kind,
                        namespace_id = record.NamespaceId,
                        title = record.Title,
                        snippet = record.Snippet,
                        source_ref = record.SourceRef,
                        document_key = record.DocumentKey,
                        event_type = record.EventType,
                        run_id = record.RunId,
                        promoted = record.Promoted,
                        created_utc = record.CreatedUtc
                    }
                }
            }
        });

        using var request = new HttpRequestMessage(HttpMethod.Put, $"collections/{_settings.ResolveQdrantCollectionName(record.NamespaceId)}/points")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        _ = response.IsSuccessStatusCode;
    }

    public async Task<IReadOnlyList<SemanticMemoryHit>> SearchAsync(string query, string documentKey, int topK, CancellationToken cancellationToken)
    {
        var vector = await _embeddingProvider.EmbedAsync(query, cancellationToken).ConfigureAwait(false);
        var hits = new List<SemanticMemoryHit>();
        foreach (var namespaceId in _settings.GetSemanticNamespaces())
        {
            hits.AddRange(await SearchCollectionAsync(namespaceId, vector.Values, documentKey, Math.Max(1, topK), cancellationToken).ConfigureAwait(false));
        }

        return hits
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.CreatedUtc, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, topK))
            .ToList();
    }

    private async Task<IReadOnlyList<SemanticMemoryHit>> SearchCollectionAsync(string namespaceId, IReadOnlyList<float> vector, string documentKey, int topK, CancellationToken cancellationToken)
    {
        var filter = string.IsNullOrWhiteSpace(documentKey)
            ? null
            : new
            {
                should = new object[]
                {
                    new { key = "document_key", match = new { value = documentKey } },
                    new { key = "document_key", match = new { value = string.Empty } }
                }
            };

        var payload = JsonSerializer.Serialize(new
        {
            vector,
            limit = topK,
            with_payload = true,
            filter
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, $"collections/{_settings.ResolveQdrantCollectionName(namespaceId)}/points/search")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return Array.Empty<SemanticMemoryHit>();
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!document.RootElement.TryGetProperty("result", out var resultNode) || resultNode.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<SemanticMemoryHit>();
        }

        var hits = new List<SemanticMemoryHit>();
        foreach (var item in resultNode.EnumerateArray())
        {
            var payloadNode = item.TryGetProperty("payload", out var payloadValue) ? payloadValue : default;
            hits.Add(new SemanticMemoryHit
            {
                Id = item.TryGetProperty("id", out var idNode) ? idNode.ToString() : string.Empty,
                Kind = TryGetPayloadString(payloadNode, "kind"),
                Namespace = string.IsNullOrWhiteSpace(TryGetPayloadString(payloadNode, "namespace_id")) ? namespaceId : TryGetPayloadString(payloadNode, "namespace_id"),
                Title = TryGetPayloadString(payloadNode, "title"),
                Snippet = TryGetPayloadString(payloadNode, "snippet"),
                SourceRef = TryGetPayloadString(payloadNode, "source_ref"),
                DocumentKey = TryGetPayloadString(payloadNode, "document_key"),
                CreatedUtc = TryGetPayloadString(payloadNode, "created_utc"),
                Score = item.TryGetProperty("score", out var scoreNode) && scoreNode.TryGetDouble(out var score) ? score : 0d
            });
        }

        return hits;
    }

    private static string TryGetPayloadString(JsonElement payload, string name)
    {
        return payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty(name, out var value)
            ? value.ToString()
            : string.Empty;
    }
}
