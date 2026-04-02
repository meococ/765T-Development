using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;
using BIM765T.Revit.Contracts.Proto;
using BIM765T.Revit.Contracts.Serialization;
using Grpc.Net.Client;

namespace BIM765T.Revit.McpHost;

internal static class Program
{
    private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        WriteIndented = false
    };

    private static readonly string[] CommonArgKeys =
    {
        "target_document", "targetDocument", "target_view", "targetView", "dry_run", "dryRun",
        "approval_token", "approvalToken", "expected_context", "expectedContext", "scope_descriptor", "scopeDescriptor",
        "preview_run_id", "previewRunId", "correlation_id", "correlationId", "payload"
    };

    private static async Task<int> Main(string[] args)
    {
        var host = new McpHost(GetOption(args, "--pipe") ?? BridgeConstants.DefaultWorkerHostPipeName);
        await host.RunAsync().ConfigureAwait(false);
        return 0;
    }

    private static string? GetOption(string[] args, string key)
    {
        var index = Array.FindIndex(args, x => string.Equals(x, key, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index < args.Length - 1 ? args[index + 1] : null;
    }

    private sealed class McpHost
    {
        private readonly string _pipeName;

        internal McpHost(string pipeName)
        {
            _pipeName = pipeName;
        }

        internal async Task RunAsync()
        {
            var input = Console.OpenStandardInput();
            var output = Console.OpenStandardOutput();

            while (true)
            {
                string? payload;
                try
                {
                    payload = await McpMessageProtocol.ReadMessageAsync(input).ConfigureAwait(false);
                }
                catch (InvalidOperationException ex)
                {
                    if (!await TryWriteProtocolErrorAsync(output, -32700, ex.Message).ConfigureAwait(false))
                    {
                        return;
                    }

                    continue;
                }
                catch (IOException)
                {
                    return;
                }

                if (payload == null)
                {
                    return;
                }

                JsonDocument document;
                try
                {
                    document = JsonDocument.Parse(payload);
                }
                catch (JsonException ex)
                {
                    if (!await TryWriteProtocolErrorAsync(output, -32700, ex.Message).ConfigureAwait(false))
                    {
                        return;
                    }

                    continue;
                }

                using (document)
                {
                    var root = document.RootElement;
                    var method = root.TryGetProperty("method", out var methodNode) ? methodNode.GetString() ?? string.Empty : string.Empty;
                    if (string.IsNullOrWhiteSpace(method))
                    {
                        if (!await WriteErrorAsync(output, root, -32600, "Invalid Request").ConfigureAwait(false))
                        {
                            return;
                        }

                        continue;
                    }

                    if (string.Equals(method, "notifications/initialized", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    try
                    {
                        switch (method)
                        {
                            case "initialize":
                                if (!await WriteResultAsync(output, root, new
                                {
                                    protocolVersion = ResolveProtocolVersion(root),
                                    capabilities = new { tools = new { listChanged = false } },
                                    serverInfo = new { name = BridgeConstants.McpHostName, version = BridgeConstants.McpHostVersion },
                                    instructions = "Dùng dry_run trước cho các tool mutate/file-lifecycle; sau đó gọi lại với approval_token để execute."
                                }).ConfigureAwait(false))
                                {
                                    return;
                                }
                                break;
                            case "ping":
                                if (!await WriteResultAsync(output, root, new { }).ConfigureAwait(false))
                                {
                                    return;
                                }
                                break;
                            case "tools/list":
                                try
                                {
                                    if (!await WriteResultAsync(output, root, new { tools = await LoadToolsAsync().ConfigureAwait(false) }).ConfigureAwait(false))
                                    {
                                        return;
                                    }
                                }
                                catch (InvalidOperationException ex)
                                {
                                    if (!await WriteErrorAsync(output, root, -32001, ex.Message).ConfigureAwait(false))
                                    {
                                        return;
                                    }
                                }
                                break;
                            case "tools/call":
                                if (!await HandleToolCallAsync(output, root).ConfigureAwait(false))
                                {
                                    return;
                                }
                                break;
                            default:
                                if (!await WriteErrorAsync(output, root, -32601, "Method not found").ConfigureAwait(false))
                                {
                                    return;
                                }
                                break;
                        }
                    }
                    catch (IOException)
                    {
                        return;
                    }
                }
            }
        }

        private async Task<bool> HandleToolCallAsync(Stream output, JsonElement root)
        {
            var parameters = root.TryGetProperty("params", out var paramsNode) ? paramsNode : default;
            var toolName = parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty("name", out var nameNode)
                ? nameNode.GetString() ?? string.Empty
                : string.Empty;
            var arguments = parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty("arguments", out var argumentsNode)
                ? JsonNode.Parse(argumentsNode.GetRawText()) as JsonObject ?? new JsonObject()
                : new JsonObject();

            if (string.IsNullOrWhiteSpace(toolName))
            {
                return await WriteErrorAsync(output, root, -32602, "Missing tool name").ConfigureAwait(false);
            }

            var request = BuildToolRequest(toolName, arguments);
            var response = await SendToBridgeAsync(request).ConfigureAwait(false);
            var structured = BuildStructuredResult(response);
            return await WriteResultAsync(output, root, new
            {
                content = new[] { new { type = "text", text = JsonSerializer.Serialize(structured, JsonOptions) } },
                structuredContent = structured,
                isError = !response.Succeeded
            }).ConfigureAwait(false);
        }

        private ToolRequestEnvelope BuildToolRequest(string toolName, JsonObject arguments)
        {
            var dryRun = GetBoolean(arguments, "dry_run", GetBoolean(arguments, "dryRun", true));
            var envelope = new ToolRequestEnvelope
            {
                RequestId = Guid.NewGuid().ToString("N"),
                ToolName = toolName,
                Caller = "BIM765T.Revit.McpHost",
                SessionId = "mcp-session",
                DryRun = dryRun,
                ProtocolVersion = BridgeProtocol.PipeV1,
                CorrelationId = GetString(arguments, "correlation_id") ?? GetString(arguments, "correlationId") ?? Guid.NewGuid().ToString("N"),
                TargetDocument = GetString(arguments, "target_document") ?? GetString(arguments, "targetDocument") ?? string.Empty,
                TargetView = GetString(arguments, "target_view") ?? GetString(arguments, "targetView") ?? string.Empty,
                ApprovalToken = GetString(arguments, "approval_token") ?? GetString(arguments, "approvalToken") ?? string.Empty,
                PreviewRunId = GetString(arguments, "preview_run_id") ?? GetString(arguments, "previewRunId") ?? string.Empty,
                ExpectedContextJson = SerializeNormalizedObject(GetObject(arguments, "expected_context") ?? GetObject(arguments, "expectedContext")),
                ScopeDescriptorJson = SerializeNormalizedObject(GetObject(arguments, "scope_descriptor") ?? GetObject(arguments, "scopeDescriptor"))
            };

            var payloadNode = GetObject(arguments, "payload") ?? BuildPayloadFromFlattenedArgs(arguments);
            envelope.PayloadJson = payloadNode == null || payloadNode.Count == 0 ? string.Empty : payloadNode.ToJsonString();
            return envelope;
        }

        private async Task<ToolResponseEnvelope> SendToBridgeAsync(ToolRequestEnvelope request)
        {
            try
            {
                using var channel = CreateWorkerHostChannel(_pipeName);
                var client = new CompatibilityService.CompatibilityServiceClient(channel);
                var response = await client.InvokeToolAsync(BuildCompatRequest(request)).ResponseAsync.ConfigureAwait(false);
                return new ToolResponseEnvelope
                {
                    RequestId = request.RequestId,
                    ToolName = response.ToolName,
                    CorrelationId = request.CorrelationId,
                    ProtocolVersion = string.IsNullOrWhiteSpace(response.ProtocolVersion) ? request.ProtocolVersion : response.ProtocolVersion,
                    Succeeded = response.Status?.Succeeded ?? false,
                    StatusCode = response.Status?.StatusCode ?? StatusCodes.BridgeUnavailable,
                    PayloadJson = response.PayloadJson ?? string.Empty,
                    ApprovalToken = response.ApprovalToken ?? string.Empty,
                    PreviewRunId = response.PreviewRunId ?? string.Empty,
                    DiffSummaryJson = response.DiffSummaryJson ?? string.Empty,
                    ReviewSummaryJson = response.ReviewSummaryJson ?? string.Empty,
                    ConfirmationRequired = response.ConfirmationRequired,
                    ChangedIds = response.ChangedIds.ToList(),
                    Artifacts = response.Artifacts.ToList(),
                    Diagnostics = response.Status?.Diagnostics.Select(x => DiagnosticRecord.Create("WORKERHOST", DiagnosticSeverity.Info, x)).ToList()
                        ?? new List<DiagnosticRecord>()
                };
            }
            catch (Exception ex)
            {
                return new ToolResponseEnvelope
                {
                    RequestId = request.RequestId,
                    ToolName = request.ToolName,
                    CorrelationId = request.CorrelationId,
                    ProtocolVersion = request.ProtocolVersion,
                    Succeeded = false,
                    StatusCode = StatusCodes.BridgeUnavailable,
                    Diagnostics = new List<DiagnosticRecord>
                    {
                        DiagnosticRecord.Create("MCP_BRIDGE_CONNECT_FAILED", DiagnosticSeverity.Error, ex.Message)
                    }
                };
            }
        }

        private static CompatToolRequest BuildCompatRequest(ToolRequestEnvelope request)
        {
            return new CompatToolRequest
            {
                Meta = new EnvelopeMetadata
                {
                    CorrelationId = request.CorrelationId ?? string.Empty,
                    CausationId = request.RequestId ?? string.Empty,
                    MissionId = request.PreviewRunId ?? string.Empty,
                    ActorId = request.Caller ?? string.Empty,
                    DocumentKey = request.TargetDocument ?? string.Empty,
                    RequestedAtUtc = request.RequestedAtUtc.ToString("O"),
                    TimeoutMs = BridgeConstants.DefaultRequestTimeoutSeconds * 1000,
                    SessionId = request.SessionId ?? string.Empty,
                    TargetDocument = request.TargetDocument ?? string.Empty,
                    TargetView = request.TargetView ?? string.Empty
                },
                ToolName = request.ToolName ?? string.Empty,
                PayloadJson = request.PayloadJson ?? string.Empty,
                DryRun = request.DryRun,
                ApprovalToken = request.ApprovalToken ?? string.Empty,
                ExpectedContextJson = request.ExpectedContextJson ?? string.Empty,
                PreviewRunId = request.PreviewRunId ?? string.Empty,
                ScopeDescriptorJson = request.ScopeDescriptorJson ?? string.Empty
            };
        }

        private static GrpcChannel CreateWorkerHostChannel(string pipeName)
        {
            var handler = new SocketsHttpHandler
            {
                EnableMultipleHttp2Connections = true,
                ConnectCallback = async (_, cancellationToken) =>
                {
                    var stream = new NamedPipeClientStream(
                        ".",
                        pipeName,
                        PipeDirection.InOut,
                        PipeOptions.Asynchronous,
                        System.Security.Principal.TokenImpersonationLevel.Impersonation);
                    await stream.ConnectAsync(cancellationToken).ConfigureAwait(false);
                    return stream;
                }
            };

            return GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions
            {
                HttpHandler = handler
            });
        }

        private static object BuildStructuredResult(ToolResponseEnvelope response)
        {
            return new
            {
                requestId = response.RequestId,
                correlationId = response.CorrelationId,
                toolName = response.ToolName,
                succeeded = response.Succeeded,
                statusCode = response.StatusCode,
                confirmationRequired = response.ConfirmationRequired,
                approvalToken = string.IsNullOrWhiteSpace(response.ApprovalToken) ? null : response.ApprovalToken,
                previewRunId = string.IsNullOrWhiteSpace(response.PreviewRunId) ? null : response.PreviewRunId,
                changedIds = response.ChangedIds,
                payload = ParseJsonOrString(response.PayloadJson),
                diffSummary = ParseJsonOrString(response.DiffSummaryJson),
                reviewSummary = ParseJsonOrString(response.ReviewSummaryJson),
                diagnostics = response.Diagnostics,
                artifacts = response.Artifacts
            };
        }

        private async Task<object[]> LoadToolsAsync()
        {
            var response = await SendToBridgeAsync(new ToolRequestEnvelope
            {
                RequestId = Guid.NewGuid().ToString("N"),
                ToolName = ToolNames.SessionListTools,
                PayloadJson = JsonUtil.Serialize(new ToolCatalogRequest { Audience = ToolCatalogAudiences.Mcp }),
                Caller = "BIM765T.Revit.McpHost",
                SessionId = "mcp-session",
                DryRun = true,
                CorrelationId = Guid.NewGuid().ToString("N"),
                ProtocolVersion = BridgeProtocol.PipeV1
            }).ConfigureAwait(false);

            var catalog = McpToolCatalogLoader.ParseOrThrow(response);
            return ToolCatalog.Build(catalog.Tools);
        }

        private static JsonObject? BuildPayloadFromFlattenedArgs(JsonObject arguments)
        {
            var payload = new JsonObject();
            foreach (var kvp in arguments)
            {
                if (CommonArgKeys.Contains(kvp.Key, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                payload[ToPascalCase(kvp.Key)] = NormalizeKeys(kvp.Value?.DeepClone());
            }

            return payload;
        }

        private static JsonNode? NormalizeKeys(JsonNode? node)
        {
            if (node is JsonObject obj)
            {
                var normalized = new JsonObject();
                foreach (var kvp in obj)
                {
                    normalized[ToPascalCase(kvp.Key)] = NormalizeKeys(kvp.Value);
                }
                return normalized;
            }

            if (node is JsonArray array)
            {
                var normalized = new JsonArray();
                foreach (var item in array)
                {
                    normalized.Add(NormalizeKeys(item));
                }
                return normalized;
            }

            return node;
        }

        private static string SerializeNormalizedObject(JsonObject? obj)
        {
            return obj == null ? string.Empty : NormalizeKeys(obj)?.ToJsonString() ?? string.Empty;
        }

        private static object? ParseJsonOrString(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<object>(raw, JsonOptions);
            }
            catch
            {
                return raw;
            }
        }

        private static string ResolveProtocolVersion(JsonElement root)
        {
            if (root.TryGetProperty("params", out var paramsNode) && paramsNode.ValueKind == JsonValueKind.Object && paramsNode.TryGetProperty("protocolVersion", out var versionNode))
            {
                return versionNode.GetString() ?? BridgeConstants.McpDefaultProtocolVersion;
            }
            return BridgeConstants.McpDefaultProtocolVersion;
        }
    }

    private static Task<bool> WriteResultAsync(Stream output, JsonElement requestRoot, object result)
    {
        var idNode = requestRoot.TryGetProperty("id", out var id) ? JsonNode.Parse(id.GetRawText()) : null;
        return WriteMessageAsync(output, new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = idNode,
            ["result"] = JsonSerializer.SerializeToNode(result, JsonOptions)
        });
    }

    private static Task<bool> WriteErrorAsync(Stream output, JsonElement requestRoot, int code, string message)
    {
        var idNode = requestRoot.TryGetProperty("id", out var id) ? JsonNode.Parse(id.GetRawText()) : null;
        return WriteMessageAsync(output, new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = idNode,
            ["error"] = new JsonObject { ["code"] = code, ["message"] = message }
        });
    }

    private static async Task<bool> WriteMessageAsync(Stream output, JsonObject payload)
    {
        try
        {
            await McpMessageProtocol.WriteMessageAsync(output, payload).ConfigureAwait(false);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static Task<bool> TryWriteProtocolErrorAsync(Stream output, int code, string message)
    {
        return WriteMessageAsync(output, new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = null,
            ["error"] = new JsonObject { ["code"] = code, ["message"] = message }
        });
    }

    private static string? GetString(JsonObject obj, string key)
    {
        return obj.TryGetPropertyValue(key, out var node) ? node?.GetValue<string>() : null;
    }

    private static bool GetBoolean(JsonObject obj, string key, bool fallback)
    {
        return obj.TryGetPropertyValue(key, out var node) && node is JsonValue valueNode && valueNode.TryGetValue<bool>(out var value)
            ? value
            : fallback;
    }

    private static JsonObject? GetObject(JsonObject obj, string key)
    {
        return obj.TryGetPropertyValue(key, out var node) ? node as JsonObject : null;
    }

    private static string ToPascalCase(string raw)
    {
        var parts = raw.Replace("-", "_").Split(new[] { '_' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return raw;
        }
        return string.Concat(parts.Select(x => char.ToUpperInvariant(x[0]) + x.Substring(1)));
    }
}

