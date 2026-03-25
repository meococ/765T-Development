using System;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace BIM765T.Revit.Copilot.Core.Brain;

/// <summary>
/// Generic OpenAI-compatible chat completions client.
/// Supports:
/// - OpenAI official endpoint
/// - self-hosted / proxy endpoints implementing /v1/chat/completions
/// - other compatible providers as long as they accept bearer auth + message list
/// </summary>
public sealed class OpenAiCompatibleLlmClient : ILlmClient
{
    private const string DefaultApiUrl = "https://api.openai.com/v1/chat/completions";
    private const string DefaultModel = "gpt-5-mini";
    private const int DefaultMaxTokens = 1024;
    private const int DefaultTimeoutSeconds = 12;

    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly string _apiUrl;
    private readonly int _maxTokens;
    private readonly string _providerLabel;
    private readonly string _organization;
    private readonly string _project;
    private readonly string _httpReferer;
    private readonly string _xTitle;
    private readonly bool _enableReasoningSplit;

    private static int _startupLogged;

    public OpenAiCompatibleLlmClient(
        HttpClient httpClient,
        string apiKey,
        string? model = null,
        int maxTokens = DefaultMaxTokens,
        string? apiUrl = null,
        string? providerLabel = null,
        string? organization = null,
        string? project = null,
        string? httpReferer = null,
        string? xTitle = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException("API key must not be empty.", nameof(apiKey));
        }

        _apiKey = apiKey.Trim();
        _model = string.IsNullOrWhiteSpace(model) ? DefaultModel : model?.Trim() ?? DefaultModel;
        _apiUrl = NormalizeApiUrl(apiUrl);
        _maxTokens = maxTokens > 0 ? maxTokens : DefaultMaxTokens;
        _providerLabel = string.IsNullOrWhiteSpace(providerLabel) ? "OpenAI-compatible" : providerLabel?.Trim() ?? "OpenAI-compatible";
        _organization = organization?.Trim() ?? string.Empty;
        _project = project?.Trim() ?? string.Empty;
        _httpReferer = httpReferer?.Trim() ?? string.Empty;
        _xTitle = string.IsNullOrWhiteSpace(xTitle) ? "BIM765T Revit Worker" : xTitle?.Trim() ?? "BIM765T Revit Worker";
        _enableReasoningSplit = IsMiniMaxProvider(_providerLabel, _apiUrl);
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);

    public string Model => _model;

    public async Task<string> CompleteAsync(string systemPrompt, string userMessage, CancellationToken cancellationToken)
    {
        return await SendCompletionAsync(systemPrompt, userMessage, jsonMode: false, stream: false, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> CompleteJsonAsync(string systemPrompt, string userMessage, CancellationToken cancellationToken, bool stream = false)
    {
        return await SendCompletionAsync(systemPrompt, userMessage, jsonMode: true, stream: stream, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> SendCompletionAsync(string systemPrompt, string userMessage, bool jsonMode, bool stream, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
        {
            return string.Empty;
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(DefaultTimeoutSeconds));

            using var request = new HttpRequestMessage(HttpMethod.Post, _apiUrl);
            request.Headers.Add("Authorization", $"Bearer {_apiKey}");
            if (!string.IsNullOrWhiteSpace(_organization))
            {
                request.Headers.Add("OpenAI-Organization", _organization);
            }

            if (!string.IsNullOrWhiteSpace(_project))
            {
                request.Headers.Add("OpenAI-Project", _project);
            }

            if (!string.IsNullOrWhiteSpace(_httpReferer))
            {
                request.Headers.Add("HTTP-Referer", _httpReferer);
            }

            if (!string.IsNullOrWhiteSpace(_xTitle))
            {
                request.Headers.Add("X-Title", _xTitle);
            }

            request.Content = new StringContent(BuildRequestBody(systemPrompt, userMessage, jsonMode, stream), Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request, cts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                LogAlways($"BIM765T OpenAI gateway: API returned {(int)response.StatusCode}. URL={_apiUrl} Model={_model}. Body={Truncate(error, 300)}");
                return string.Empty;
            }

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var text = ExtractText(json, jsonMode);
            LogOnce($"BIM765T OpenAI gateway connected [{_providerLabel}] model={_model}.");
            return text;
        }
        catch (OperationCanceledException)
        {
            LogAlways($"BIM765T OpenAI gateway: request timed out for provider {_providerLabel}.");
            return string.Empty;
        }
        catch (HttpRequestException ex)
        {
            LogAlways($"BIM765T OpenAI gateway: network error for provider {_providerLabel} - {ex.Message}");
            return string.Empty;
        }
        catch (Exception ex)
        {
            LogAlways($"BIM765T OpenAI gateway: unexpected error for provider {_providerLabel} - {ex.GetType().Name}: {ex.Message}");
            return string.Empty;
        }
    }

    private string BuildRequestBody(string systemPrompt, string userMessage, bool jsonMode, bool stream)
    {
        var builder = new StringBuilder(512);
        builder.Append('{');
        AppendJsonProperty(builder, "model", _model, isFirst: true);
        AppendJsonProperty(builder, "max_tokens", _maxTokens);
        if (_enableReasoningSplit && jsonMode)
        {
            AppendJsonProperty(builder, "reasoning_split", true);
        }

        if (jsonMode)
        {
            builder.Append(",\"response_format\":{\"type\":\"json_object\"}");
        }

        if (stream)
        {
            AppendJsonProperty(builder, "stream", true);
        }

        builder.Append(",\"messages\":[");
        var hasSystemPrompt = !string.IsNullOrWhiteSpace(systemPrompt);
        if (hasSystemPrompt)
        {
            builder.Append("{\"role\":\"system\",\"content\":");
            AppendJsonString(builder, systemPrompt);
            builder.Append('}');
            builder.Append(',');
        }

        builder.Append("{\"role\":\"user\",\"content\":");
        AppendJsonString(builder, userMessage);
        builder.Append("}]}");
        return builder.ToString();
    }

    private static string ExtractText(string responseJson, bool preferJson)
    {
        try
        {
            if (!LightweightJson.TryParse(responseJson, out var root)
                || !root.TryGetProperty("choices", out var choices)
                || !choices.IsArray
                || choices.Items.Count == 0)
            {
                return string.Empty;
            }

            var first = choices.Items[0];
            if (!first.TryGetProperty("message", out var message)
                || !message.TryGetProperty("content", out var content))
            {
                return string.Empty;
            }

            if (content.Kind == LightweightJsonKind.String)
            {
                return SanitizeContent(content.StringValue, preferJson);
            }

            if (content.IsArray)
            {
                var builder = new StringBuilder();
                foreach (var item in content.Items)
                {
                    if (item.Kind == LightweightJsonKind.String)
                    {
                        builder.AppendLine(item.StringValue);
                    }
                    else if (item.IsObject
                        && item.TryGetProperty("text", out var text)
                        && text.Kind == LightweightJsonKind.String)
                    {
                        builder.AppendLine(text.StringValue);
                    }
                }

                return SanitizeContent(builder.ToString(), preferJson);
            }
        }
        catch
        {
            // fall through
        }

        return string.Empty;
    }

    private static string NormalizeApiUrl(string? apiUrl)
    {
        var candidate = string.IsNullOrWhiteSpace(apiUrl) ? DefaultApiUrl : apiUrl?.Trim() ?? DefaultApiUrl;
        if (candidate.IndexOf("/chat/completions", StringComparison.OrdinalIgnoreCase) >= 0
            || candidate.IndexOf("/responses", StringComparison.OrdinalIgnoreCase) >= 0
            || candidate.IndexOf("/v1/messages", StringComparison.OrdinalIgnoreCase) >= 0
            || candidate.IndexOf("/anthropic", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return candidate;
        }

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
        {
            return candidate;
        }

        var normalizedPath = uri.AbsolutePath.TrimEnd('/');
        if (!normalizedPath.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
        {
            return candidate;
        }

        var builder = new UriBuilder(uri)
        {
            Path = normalizedPath + "/chat/completions"
        };

        return builder.Uri.ToString().TrimEnd('/');
    }

    private static string SanitizeContent(string raw, bool preferJson)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var sanitized = raw.Trim();
        sanitized = Regex.Replace(sanitized, @"<think>.*?</think>\s*", string.Empty, RegexOptions.Singleline | RegexOptions.IgnoreCase);
        sanitized = Regex.Replace(sanitized, @"^```(?:json)?\s*|\s*```$", string.Empty, RegexOptions.Singleline | RegexOptions.IgnoreCase).Trim();

        if (!preferJson)
        {
            return sanitized;
        }

        var extractedJson = TryExtractJsonObject(sanitized);
        return string.IsNullOrWhiteSpace(extractedJson) ? sanitized : extractedJson;
    }

    private static string TryExtractJsonObject(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var start = text.IndexOf('{');
        if (start < 0)
        {
            return string.Empty;
        }

        var depth = 0;
        var inString = false;
        var escaping = false;
        for (var i = start; i < text.Length; i++)
        {
            var ch = text[i];
            if (escaping)
            {
                escaping = false;
                continue;
            }

            if (ch == '\\')
            {
                escaping = true;
                continue;
            }

            if (ch == '"')
            {
                inString = !inString;
                continue;
            }

            if (inString)
            {
                continue;
            }

            if (ch == '{')
            {
                depth++;
            }
            else if (ch == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return text.Substring(start, i - start + 1).Trim();
                }
            }
        }

        return string.Empty;
    }

    private static bool IsMiniMaxProvider(string providerLabel, string apiUrl)
    {
        return providerLabel.IndexOf("MINIMAX", StringComparison.OrdinalIgnoreCase) >= 0
            || apiUrl.IndexOf("minimax", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static void AppendJsonProperty(StringBuilder builder, string name, string value, bool isFirst = false)
    {
        if (!isFirst)
        {
            builder.Append(',');
        }

        builder.Append('"').Append(name).Append("\":");
        AppendJsonString(builder, value);
    }

    private static void AppendJsonProperty(StringBuilder builder, string name, int value)
    {
        builder.Append(",\"").Append(name).Append("\":").Append(value);
    }

    private static void AppendJsonProperty(StringBuilder builder, string name, bool value)
    {
        builder.Append(",\"").Append(name).Append("\":").Append(value ? "true" : "false");
    }

    private static void AppendJsonString(StringBuilder builder, string? value)
    {
        builder.Append('"');
        foreach (var ch in value ?? string.Empty)
        {
            switch (ch)
            {
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\b':
                    builder.Append("\\b");
                    break;
                case '\f':
                    builder.Append("\\f");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    if (char.IsControl(ch))
                    {
                        builder.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(ch);
                    }

                    break;
            }
        }

        builder.Append('"');
    }

    private static void LogOnce(string message)
    {
        if (Interlocked.CompareExchange(ref _startupLogged, 1, 0) == 0)
        {
            Trace.TraceInformation(message);
        }
    }

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
