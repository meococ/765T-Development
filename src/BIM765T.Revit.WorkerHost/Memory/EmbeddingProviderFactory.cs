using System;
using System.Net.Http;
using BIM765T.Revit.Copilot.Core.Brain;
using BIM765T.Revit.WorkerHost.Configuration;
using Microsoft.Extensions.Logging;

namespace BIM765T.Revit.WorkerHost.Memory;

/// <summary>
/// Holds the resolved embedding client + provider pair from factory creation.
/// Registered as singleton in DI so both IEmbeddingClient and IEmbeddingProvider resolve to same instance.
/// </summary>
internal sealed class EmbeddingProviderResult
{
    public IEmbeddingClient Client { get; init; } = null!;
    public IEmbeddingProvider Provider { get; init; } = null!;
    public string DiagnosticMessage { get; init; } = string.Empty;
}

/// <summary>
/// Resolves the best available embedding provider at startup.
/// Priority: Ollama (semantic, local) -> HashEmbeddingClient (lexical fallback).
///
/// OpenAI/Azure can be added as intermediate priority later when needed.
/// The factory probes Ollama availability synchronously at DI registration time.
/// </summary>
internal sealed class EmbeddingProviderFactory
{
    private static readonly Action<ILogger, string, string, Exception?> OllamaRegisteredLog =
        LoggerMessage.Define<string, string>(
            LogLevel.Information,
            new EventId(5010, "OllamaEmbeddingClientRegistered"),
            "Using OllamaEmbeddingClient (semantic). URL={OllamaUrl}, model={OllamaModel}.");

    private static readonly Action<ILogger, string, string, Exception?> OllamaUnreachableLog =
        LoggerMessage.Define<string, string>(
            LogLevel.Warning,
            new EventId(5011, "OllamaEmbeddingClientUnreachable"),
            "Ollama configured at {OllamaUrl} but unreachable ({ErrorDetail}). Falling back to hash embeddings.");

    private static readonly Action<ILogger, Exception?> HashFallbackLog =
        LoggerMessage.Define(
            LogLevel.Warning,
            new EventId(5001, "HashEmbeddingClientRegistered"),
            "Using HashEmbeddingClient (non-semantic). Qdrant search results are lexical-hash-based, " +
            "not semantic. Configure Ollama for production.");

    /// <summary>
    /// Creates the best available embedding provider.
    /// Checks OllamaUrl reachability; falls back to hash if unreachable.
    /// </summary>
    public static EmbeddingProviderResult Create(
        WorkerHostSettings settings, IHttpClientFactory httpClientFactory, ILogger logger, ICopilotLogger? copilotLogger = null, LlmTimeoutProfile? timeoutProfile = null)
    {
        // 1. Try Ollama if URL is configured
        var ollamaUrl = ResolveOllamaUrl(settings);
        if (!string.IsNullOrWhiteSpace(ollamaUrl))
        {
            try
            {
                var httpClient = httpClientFactory.CreateClient("ollama-embedding");
                httpClient.BaseAddress = new Uri(ollamaUrl.EndsWith('/') ? ollamaUrl : ollamaUrl + "/");
                httpClient.Timeout = TimeSpan.FromSeconds(20);

                var model = ResolveOllamaModel(settings);
                var client = new OllamaEmbeddingClient(httpClient, model, copilotLogger, timeoutProfile);
                ProbeOllamaAvailability(ollamaUrl);
                OllamaRegisteredLog(logger, ollamaUrl, model, null);
                return new EmbeddingProviderResult { Client = client, Provider = client, DiagnosticMessage = $"ollama:{model}@{ollamaUrl}" };
            }
            catch (Exception ex)
            {
                OllamaUnreachableLog(logger, ollamaUrl, $"{ex.GetType().Name}: {ex.Message}", null);
            }
        }

        // 2. Fallback: hash embeddings (non-semantic)
        var hash = new HashEmbeddingClient(settings.EmbeddingDimensions);
        HashFallbackLog(logger, null);
        return new EmbeddingProviderResult { Client = hash, Provider = hash, DiagnosticMessage = "hash_lexical_fallback" };
    }

    private static string ResolveOllamaUrl(WorkerHostSettings settings)
    {
        // Env var takes precedence, then settings property
        var envUrl = Environment.GetEnvironmentVariable("OLLAMA_URL")
                     ?? Environment.GetEnvironmentVariable("OLLAMA_HOST");
        if (!string.IsNullOrWhiteSpace(envUrl))
        {
            return envUrl.Trim();
        }

        return settings.OllamaUrl ?? string.Empty;
    }

    private static string ResolveOllamaModel(WorkerHostSettings settings)
    {
        var envModel = Environment.GetEnvironmentVariable("OLLAMA_EMBEDDING_MODEL");
        if (!string.IsNullOrWhiteSpace(envModel))
        {
            return envModel.Trim();
        }

        return settings.OllamaEmbeddingModel ?? "nomic-embed-text";
    }

    /// <summary>
    /// Quick probe: hit Ollama /api/tags to check it's alive.
    /// Throws if unreachable so caller falls back to hash.
    /// Uses synchronous Send() to avoid sync-over-async deadlock during DI registration.
    /// </summary>
    private static void ProbeOllamaAvailability(string baseUrl)
    {
        using var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        var url = baseUrl.TrimEnd('/') + "/api/tags";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = probe.Send(request);
        response.EnsureSuccessStatusCode();
    }
}
