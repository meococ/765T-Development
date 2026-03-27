using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BIM765T.Revit.Copilot.Core.Brain;

/// <summary>
/// Production <see cref="ILlmClient"/> implementation that calls Claude APIs.
/// Supports two modes, auto-detected from the API URL:
///   1. Anthropic Messages API (api.anthropic.com/v1/messages)
///   2. OpenAI-compatible Chat Completions (e.g. Claudible, OpenRouter, LiteLLM)
///
/// Uses raw <see cref="HttpClient"/> (no SDK) so this stays netstandard2.0 compatible.
///
/// Graceful degradation:
/// - If the API call fails (network, auth, rate-limit, timeout) the caller receives
///   an empty string, never an exception. The caller is expected to fall back to
///   rule-based text when the return value is empty or whitespace.
/// - A one-time startup trace is emitted on the first successful or failed call
///   so operators can confirm the LLM path is active in production logs.
/// </summary>
public sealed class AnthropicLlmClient : ILlmClient
{
    private const string DefaultAnthropicUrl = "https://api.anthropic.com/v1/messages";
    private const string DefaultAnthropicModel = "claude-sonnet-4-20250514";
    private const string DefaultOpenAiModel = "claude-sonnet-4.6";
    private const int DefaultMaxTokens = 1024;
    private const int DefaultTimeoutSeconds = 20;
    private const string AnthropicVersion = "2023-06-01";

    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly int _maxTokens;
    private readonly string _apiUrl;
    private readonly bool _useOpenAiFormat;

    // One-time startup log via CAS — same pattern as NullLlmClient.
    private static int _startupLogged;

    /// <summary>
    /// Creates a new Claude LLM client.
    /// Auto-detects API format from the URL:
    /// - URLs containing "/v1/messages" → Anthropic Messages API
    /// - All others (including /v1/chat/completions) → OpenAI-compatible format
    /// </summary>
    /// <param name="httpClient">
    /// A shared <see cref="HttpClient"/> instance. Caller owns lifetime.
    /// The client sets per-request headers so the same HttpClient can be reused elsewhere.
    /// </param>
    /// <param name="apiKey">API key (required, non-empty).</param>
    /// <param name="model">Model identifier. Auto-selected based on API format if null.</param>
    /// <param name="maxTokens">Max tokens per completion. Defaults to 1024.</param>
    /// <param name="apiUrl">Override API endpoint. Defaults to Anthropic production.</param>
    public AnthropicLlmClient(
        HttpClient httpClient,
        string apiKey,
        string? model = null,
        int maxTokens = DefaultMaxTokens,
        string? apiUrl = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException("API key must not be empty.", nameof(apiKey));
        }

        _apiKey = apiKey.Trim();
        _maxTokens = maxTokens > 0 ? maxTokens : DefaultMaxTokens;

        // Determine API format from URL
        var resolvedUrl = NormalizeApiUrl(string.IsNullOrWhiteSpace(apiUrl) ? DefaultAnthropicUrl : apiUrl!.Trim());
        _useOpenAiFormat = !resolvedUrl.Contains("/v1/messages");

        // If URL is a base like "https://claudible.io/v1/messages" but that's Claudible → use chat completions
        // If URL ends with /v1/chat/completions → OpenAI format
        // If URL ends with /v1/messages → Anthropic format
        _apiUrl = resolvedUrl;

        // Auto-select model based on format if not specified
        if (!string.IsNullOrWhiteSpace(model))
        {
            _model = model!.Trim();
        }
        else
        {
            _model = _useOpenAiFormat ? DefaultOpenAiModel : DefaultAnthropicModel;
        }
    }

    /// <inheritdoc />
    public async Task<string> CompleteAsync(string systemPrompt, string userMessage, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
        {
            return string.Empty;
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(DefaultTimeoutSeconds));

            var requestBody = _useOpenAiFormat
                ? BuildOpenAiRequestBody(systemPrompt, userMessage)
                : BuildAnthropicRequestBody(systemPrompt, userMessage);

            using var request = new HttpRequestMessage(HttpMethod.Post, _apiUrl);

            if (_useOpenAiFormat)
            {
                request.Headers.Add("Authorization", $"Bearer {_apiKey}");
                // OpenRouter requires HTTP-Referer and X-Title for attribution
                if (_apiUrl.Contains("openrouter.ai"))
                {
                    request.Headers.Add("HTTP-Referer", "https://github.com/meococ/Develop-Revit-API");
                    request.Headers.Add("X-Title", "BIM765T Revit Worker");
                }
            }
            else
            {
                request.Headers.Add("x-api-key", _apiKey);
                request.Headers.Add("anthropic-version", AnthropicVersion);
            }

            request.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request, cts.Token).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                LogAlways($"BIM765T LlmClient: API returned {(int)response.StatusCode}. URL={_apiUrl} Model={_model} Body: {Truncate(errorBody, 300)}");
                return string.Empty;
            }

            var responseJson = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var text = _useOpenAiFormat
                ? ExtractOpenAiText(responseJson)
                : ExtractAnthropicText(responseJson);

            LogOnce($"BIM765T LlmClient: Connected [{(_useOpenAiFormat ? "OpenAI-compat" : "Anthropic")}] model={_model}. LLM enhancement active.");
            return text;
        }
        catch (OperationCanceledException)
        {
            LogAlways("BIM765T LlmClient: Request timed out or was cancelled. Falling back to rule-based text.");
            return string.Empty;
        }
        catch (HttpRequestException ex)
        {
            LogAlways($"BIM765T LlmClient: Network error — {ex.Message}. Falling back to rule-based text.");
            return string.Empty;
        }
        catch (Exception ex)
        {
            LogAlways($"BIM765T LlmClient: Unexpected error — {ex.GetType().Name}: {ex.Message}. Falling back to rule-based text.");
            return string.Empty;
        }
    }

    /// <summary>
    /// Returns true if this client has a valid API key configured.
    /// </summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);

    /// <summary>The model identifier this client will call.</summary>
    public string Model => _model;

    /// <summary>True if using OpenAI-compatible format (Claudible, OpenRouter, etc.).</summary>
    public bool IsOpenAiCompatible => _useOpenAiFormat;

    // ────────────────────────────────────────────────────
    // Anthropic Messages API format
    // ────────────────────────────────────────────────────

    private string BuildAnthropicRequestBody(string systemPrompt, string userMessage)
    {
        using var stream = new System.IO.MemoryStream();
        using var writer = new Utf8JsonWriter(stream);
        writer.WriteStartObject();
        writer.WriteString("model", _model);
        writer.WriteNumber("max_tokens", _maxTokens);

        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            writer.WriteString("system", systemPrompt);
        }

        writer.WriteStartArray("messages");
        writer.WriteStartObject();
        writer.WriteString("role", "user");
        writer.WriteString("content", userMessage);
        writer.WriteEndObject();
        writer.WriteEndArray();

        writer.WriteEndObject();
        writer.Flush();

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string ExtractAnthropicText(string responseJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseJson);
            var root = doc.RootElement;
            if (root.TryGetProperty("content", out var content) && content.GetArrayLength() > 0)
            {
                var firstBlock = content[0];
                if (firstBlock.TryGetProperty("text", out var textElement))
                {
                    return textElement.GetString() ?? string.Empty;
                }
            }
        }
        catch
        {
            // Parse failure — fall through to empty
        }

        return string.Empty;
    }

    private static string NormalizeApiUrl(string apiUrl)
    {
        if (string.IsNullOrWhiteSpace(apiUrl))
        {
            return DefaultAnthropicUrl;
        }

        var candidate = apiUrl.Trim();
        if (candidate.IndexOf("/v1/messages", StringComparison.OrdinalIgnoreCase) >= 0
            || candidate.IndexOf("/chat/completions", StringComparison.OrdinalIgnoreCase) >= 0
            || candidate.IndexOf("/responses", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return candidate;
        }

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
        {
            return candidate;
        }

        var normalizedPath = uri.AbsolutePath.TrimEnd('/');
        if (normalizedPath.EndsWith("/anthropic", StringComparison.OrdinalIgnoreCase))
        {
            return new UriBuilder(uri) { Path = normalizedPath + "/v1/messages" }.Uri.ToString().TrimEnd('/');
        }

        if (normalizedPath.EndsWith("/anthropic/v1", StringComparison.OrdinalIgnoreCase))
        {
            return new UriBuilder(uri) { Path = normalizedPath + "/messages" }.Uri.ToString().TrimEnd('/');
        }

        if (normalizedPath.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
            && (uri.Host.IndexOf("anthropic", StringComparison.OrdinalIgnoreCase) >= 0
                || uri.AbsolutePath.IndexOf("/anthropic", StringComparison.OrdinalIgnoreCase) >= 0))
        {
            return new UriBuilder(uri) { Path = normalizedPath + "/messages" }.Uri.ToString().TrimEnd('/');
        }

        return candidate;
    }

    // ────────────────────────────────────────────────────
    // OpenAI Chat Completions format (Claudible, OpenRouter, LiteLLM, etc.)
    // ────────────────────────────────────────────────────

    private string BuildOpenAiRequestBody(string systemPrompt, string userMessage)
    {
        using var stream = new System.IO.MemoryStream();
        using var writer = new Utf8JsonWriter(stream);
        writer.WriteStartObject();
        writer.WriteString("model", _model);
        writer.WriteNumber("max_tokens", _maxTokens);

        writer.WriteStartArray("messages");

        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            writer.WriteStartObject();
            writer.WriteString("role", "system");
            writer.WriteString("content", systemPrompt);
            writer.WriteEndObject();
        }

        writer.WriteStartObject();
        writer.WriteString("role", "user");
        writer.WriteString("content", userMessage);
        writer.WriteEndObject();

        writer.WriteEndArray();

        writer.WriteEndObject();
        writer.Flush();

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string ExtractOpenAiText(string responseJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseJson);
            var root = doc.RootElement;
            // OpenAI format: { "choices": [{ "message": { "content": "..." } }] }
            if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
            {
                var firstChoice = choices[0];
                if (firstChoice.TryGetProperty("message", out var message)
                    && message.TryGetProperty("content", out var contentElement))
                {
                    return contentElement.GetString() ?? string.Empty;
                }
            }
        }
        catch
        {
            // Parse failure — fall through to empty
        }

        return string.Empty;
    }

    // ────────────────────────────────────────────────────
    // Helpers
    // ────────────────────────────────────────────────────

    /// <summary>
    /// Log the first successful connection once (startup confirmation).
    /// Errors always log (via <see cref="LogAlways"/>) for debuggability.
    /// </summary>
    private static void LogOnce(string message)
    {
        if (Interlocked.CompareExchange(ref _startupLogged, 1, 0) == 0)
        {
            Trace.TraceInformation(message);
        }
    }

    /// <summary>Log every time — used for errors so they are not silently swallowed.</summary>
    private static void LogAlways(string message)
    {
        Trace.TraceWarning(message);
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value ?? string.Empty;
        }

        return value.Substring(0, maxLength) + "...";
    }
}
