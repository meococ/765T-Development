using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BIM765T.Revit.Contracts.Platform;
using BIM765T.Revit.Contracts.Serialization;

namespace BIM765T.Revit.Agent.UI.Chat;

internal sealed class WorkerHostMissionClient : IDisposable
{
    private readonly HttpClient _httpClient;

    internal WorkerHostMissionClient(string? baseUrl = null, HttpMessageHandler? handler = null)
    {
        var root = string.IsNullOrWhiteSpace(baseUrl) ? "http://localhost:50765" : baseUrl!.Trim().TrimEnd('/');
        _httpClient = handler == null ? new HttpClient() : new HttpClient(handler);
        _httpClient.BaseAddress = new Uri(root, UriKind.Absolute);
        _httpClient.Timeout = TimeSpan.FromSeconds(120);
    }

    internal Task<WorkerHostGatewayStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        return SendAsync<WorkerHostGatewayStatus>(HttpMethod.Get, "/api/external-ai/status", null, cancellationToken);
    }

    internal Task EnsureAvailableAsync(CancellationToken cancellationToken)
    {
        return WorkerHostRuntimeBootstrapper.EnsureAvailableAsync(_httpClient.BaseAddress, cancellationToken);
    }

    internal Task<WorkerHostMissionResponse> SubmitChatAsync(WorkerHostChatRequest request, CancellationToken cancellationToken)
    {
        return SendAsync<WorkerHostMissionResponse>(HttpMethod.Post, "/api/external-ai/chat", request, cancellationToken);
    }

    internal Task<WorkerHostMissionResponse> GetMissionAsync(string missionId, CancellationToken cancellationToken)
    {
        return SendAsync<WorkerHostMissionResponse>(HttpMethod.Get, "/api/external-ai/missions/" + Uri.EscapeDataString(missionId), null, cancellationToken);
    }

    internal Task<ProjectInitPreviewResponse> PreviewProjectInitAsync(ProjectInitPreviewRequest request, CancellationToken cancellationToken)
    {
        return SendAsync<ProjectInitPreviewResponse>(HttpMethod.Post, "/api/projects/init/preview", request, cancellationToken);
    }

    internal Task<ProjectInitApplyResponse> ApplyProjectInitAsync(ProjectInitApplyRequest request, CancellationToken cancellationToken)
    {
        return SendAsync<ProjectInitApplyResponse>(HttpMethod.Post, "/api/projects/init/apply", request, cancellationToken);
    }

    internal Task<ProjectContextBundleResponse> GetProjectContextBundleAsync(string workspaceId, string query, int maxSourceRefs, int maxStandardsRefs, CancellationToken cancellationToken)
    {
        var path =
            "/api/projects/" + Uri.EscapeDataString(workspaceId ?? string.Empty) +
            "/context?query=" + Uri.EscapeDataString(query ?? string.Empty) +
            "&maxSourceRefs=" + Math.Max(1, maxSourceRefs).ToString(System.Globalization.CultureInfo.InvariantCulture) +
            "&maxStandardsRefs=" + Math.Max(1, maxStandardsRefs).ToString(System.Globalization.CultureInfo.InvariantCulture);
        return SendAsync<ProjectContextBundleResponse>(HttpMethod.Get, path, null, cancellationToken);
    }

    internal Task<ProjectDeepScanResponse> RunProjectDeepScanAsync(string workspaceId, ProjectDeepScanRequest request, CancellationToken cancellationToken)
    {
        return SendAsync<ProjectDeepScanResponse>(
            HttpMethod.Post,
            "/api/projects/" + Uri.EscapeDataString(workspaceId ?? string.Empty) + "/deep-scan",
            request,
            cancellationToken);
    }

    internal Task<ProjectDeepScanReportResponse> GetProjectDeepScanAsync(string workspaceId, CancellationToken cancellationToken)
    {
        return SendAsync<ProjectDeepScanReportResponse>(
            HttpMethod.Get,
            "/api/projects/" + Uri.EscapeDataString(workspaceId ?? string.Empty) + "/deep-scan",
            null,
            cancellationToken);
    }

    [SuppressMessage("Reliability", "CA2016:Forward the CancellationToken parameter to methods that take one", Justification = "Stream and reader APIs used here do not expose cancellation overloads on the add-in target.")]
    internal async Task StreamMissionEventsAsync(string missionId, Func<WorkerHostMissionEvent, Task> onEvent, CancellationToken cancellationToken)
    {
        try
        {
            await StreamMissionEventsCoreAsync(missionId, onEvent, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException) when (!cancellationToken.IsCancellationRequested)
        {
            await EnsureAvailableAsync(cancellationToken).ConfigureAwait(false);
            await StreamMissionEventsCoreAsync(missionId, onEvent, cancellationToken).ConfigureAwait(false);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await EnsureAvailableAsync(cancellationToken).ConfigureAwait(false);
            await StreamMissionEventsCoreAsync(missionId, onEvent, cancellationToken).ConfigureAwait(false);
        }
    }

    [SuppressMessage("Reliability", "CA2016:Forward the CancellationToken parameter to methods that take one", Justification = "Stream and reader APIs used here do not expose cancellation overloads on the add-in target.")]
    private async Task StreamMissionEventsCoreAsync(string missionId, Func<WorkerHostMissionEvent, Task> onEvent, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/external-ai/missions/" + Uri.EscapeDataString(missionId) + "/events");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            using (stream)
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                var json = await reader.ReadToEndAsync().ConfigureAwait(false);
                throw new InvalidDataException(ExtractErrorMessage(json, response.ReasonPhrase));
            }
        }

        using (stream)
        using (var reader = new StreamReader(stream, Encoding.UTF8))
        {
            var dataBuffer = new StringBuilder();
            while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync().ConfigureAwait(false);
                if (line == null)
                {
                    break;
                }

                if (line.StartsWith("data:", StringComparison.Ordinal))
                {
                    dataBuffer.Append(line.Substring(5).TrimStart());
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                if (dataBuffer.Length == 0)
                {
                    continue;
                }

                var missionEvent = JsonUtil.DeserializeRequired<WorkerHostMissionEvent>(dataBuffer.ToString());
                dataBuffer.Clear();
                await onEvent(missionEvent).ConfigureAwait(false);
                if (missionEvent.Terminal)
                {
                    return;
                }
            }

            if (dataBuffer.Length > 0)
            {
                var missionEvent = JsonUtil.DeserializeRequired<WorkerHostMissionEvent>(dataBuffer.ToString());
                await onEvent(missionEvent).ConfigureAwait(false);
            }
        }
    }

    internal Task<WorkerHostMissionResponse> ApproveAsync(string missionId, WorkerHostMissionCommandRequest request, CancellationToken cancellationToken)
    {
        return SendAsync<WorkerHostMissionResponse>(HttpMethod.Post, "/api/external-ai/missions/" + Uri.EscapeDataString(missionId) + "/approve", request, cancellationToken);
    }

    internal Task<WorkerHostMissionResponse> RejectAsync(string missionId, WorkerHostMissionCommandRequest request, CancellationToken cancellationToken)
    {
        return SendAsync<WorkerHostMissionResponse>(HttpMethod.Post, "/api/external-ai/missions/" + Uri.EscapeDataString(missionId) + "/reject", request, cancellationToken);
    }

    internal Task<WorkerHostMissionResponse> CancelAsync(string missionId, WorkerHostMissionCommandRequest request, CancellationToken cancellationToken)
    {
        return SendAsync<WorkerHostMissionResponse>(HttpMethod.Post, "/api/external-ai/missions/" + Uri.EscapeDataString(missionId) + "/cancel", request, cancellationToken);
    }

    internal Task<WorkerHostMissionResponse> ResumeAsync(string missionId, WorkerHostMissionCommandRequest request, CancellationToken cancellationToken)
    {
        return SendAsync<WorkerHostMissionResponse>(HttpMethod.Post, "/api/external-ai/missions/" + Uri.EscapeDataString(missionId) + "/resume", request, cancellationToken);
    }

    [SuppressMessage("Reliability", "CA2016:Forward the CancellationToken parameter to methods that take one", Justification = "ReadAsStringAsync has no CancellationToken overload on the add-in target.")]
    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        try
        {
            return await SendCoreAsync<T>(method, path, body, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException) when (!cancellationToken.IsCancellationRequested)
        {
            await EnsureAvailableAsync(cancellationToken).ConfigureAwait(false);
            return await SendCoreAsync<T>(method, path, body, cancellationToken).ConfigureAwait(false);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await EnsureAvailableAsync(cancellationToken).ConfigureAwait(false);
            return await SendCoreAsync<T>(method, path, body, cancellationToken).ConfigureAwait(false);
        }
    }

    [SuppressMessage("Reliability", "CA2016:Forward the CancellationToken parameter to methods that take one", Justification = "ReadAsStringAsync has no CancellationToken overload on the add-in target.")]
    private async Task<T> SendCoreAsync<T>(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (body != null)
        {
            request.Content = new StringContent(JsonUtil.Serialize(body), Encoding.UTF8, "application/json");
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidDataException(ExtractErrorMessage(json, response.ReasonPhrase));
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            return Activator.CreateInstance<T>();
        }

        return JsonUtil.DeserializeRequired<T>(json);
    }

    private static string ExtractErrorMessage(string? json, string? fallback)
    {
        if (!string.IsNullOrWhiteSpace(json))
        {
            var payloadJson = json!;
            var message = TryReadJsonStringValue(payloadJson, "message");
            if (!string.IsNullOrWhiteSpace(message))
            {
                return message;
            }

            var statusCode = TryReadJsonStringValue(payloadJson, "statusCode");
            if (!string.IsNullOrWhiteSpace(statusCode))
            {
                return statusCode;
            }

            try
            {
                var payload = JsonUtil.DeserializeRequired<ErrorEnvelope>(json);
                if (!string.IsNullOrWhiteSpace(payload.Message))
                {
                    return payload.Message;
                }

                if (!string.IsNullOrWhiteSpace(payload.StatusCode))
                {
                    return payload.StatusCode;
                }
            }
            catch
            {
            }
        }

        return string.IsNullOrWhiteSpace(fallback) ? "WorkerHost request failed." : fallback!;
    }

    private static string TryReadJsonStringValue(string json, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(propertyName))
        {
            return string.Empty;
        }

        var pattern = "\"" + Regex.Escape(propertyName) + "\"\\s*:\\s*\"(?<value>(?:\\\\.|[^\"])*)\"";
        var match = Regex.Match(json, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success ? Regex.Unescape(match.Groups["value"].Value) : string.Empty;
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    [DataContract]
    private sealed class ErrorEnvelope
    {
        [DataMember(Order = 1)]
        public string StatusCode { get; set; } = string.Empty;

        [DataMember(Order = 2)]
        public string Message { get; set; } = string.Empty;
    }
}
