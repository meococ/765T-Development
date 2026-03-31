using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BIM765T.Revit.Copilot.Core.Brain;

namespace BIM765T.Revit.WorkerHost.Memory;

/// <summary>
/// Semantic embedding client using a local Ollama instance.
/// Calls POST /api/embed with the configured model (default: nomic-embed-text).
/// Returns real semantic vectors — semantically similar sentences produce similar vectors.
///
/// Requires Ollama running locally: <c>ollama serve</c> then <c>ollama pull nomic-embed-text</c>.
/// </summary>
internal sealed class OllamaEmbeddingClient : IEmbeddingClient, IEmbeddingProvider
{
    private readonly HttpClient _httpClient;
    private readonly string _model;
    private readonly ICopilotLogger _logger;
    private readonly int _embeddingTimeoutSeconds;

    public OllamaEmbeddingClient(HttpClient httpClient, string model, ICopilotLogger? logger = null, LlmTimeoutProfile? timeoutProfile = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _model = string.IsNullOrWhiteSpace(model) ? "nomic-embed-text" : model.Trim();
        _logger = logger ?? TraceCopilotLogger.Instance;
        _embeddingTimeoutSeconds = (timeoutProfile ?? LlmTimeoutProfile.Default).EmbeddingTimeoutSeconds;
    }

    public string ProviderId => "ollama";

    public bool IsSemantic => true;

    public async Task<EmbeddingVector> EmbedAsync(string text, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(new
        {
            model = _model,
            input = text ?? string.Empty
        });

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(_embeddingTimeoutSeconds));

        using var request = new HttpRequestMessage(HttpMethod.Post, "api/embed")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };

        using var response = await _httpClient.SendAsync(request, cts.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cts.Token).ConfigureAwait(false);

        // Ollama /api/embed returns: { "model": "...", "embeddings": [[0.1, 0.2, ...]] }
        if (!document.RootElement.TryGetProperty("embeddings", out var embeddings)
            || embeddings.ValueKind != JsonValueKind.Array
            || embeddings.GetArrayLength() == 0)
        {
            _logger.Warn("OllamaEmbeddingClient: Response missing 'embeddings' array.");
            return new EmbeddingVector();
        }

        var firstEmbedding = embeddings[0];
        if (firstEmbedding.ValueKind != JsonValueKind.Array)
        {
            _logger.Warn("OllamaEmbeddingClient: First embedding is not an array.");
            return new EmbeddingVector();
        }

        var values = new float[firstEmbedding.GetArrayLength()];
        var index = 0;
        foreach (var element in firstEmbedding.EnumerateArray())
        {
            values[index++] = element.TryGetSingle(out var f) ? f : 0f;
        }

        _logger.Info(string.Format(System.Globalization.CultureInfo.InvariantCulture, "OllamaEmbeddingClient: Embedded {0} chars -> {1} dims via {2}.", (text ?? "").Length, values.Length, _model));
        return new EmbeddingVector { Values = values };
    }
}
