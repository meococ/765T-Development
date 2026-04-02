using System;
using System.IO;
using System.IO.Pipes;
using System.Net.Http;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Proto;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;
using BIM765T.Revit.Contracts.Serialization;
using Google.Protobuf;
using Grpc.Net.Client;

namespace BIM765T.Revit.Bridge;

internal static class Program
{
    private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;
        var rawCommand = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.OrdinalIgnoreCase)) ?? ToolNames.DocumentGetActive;
        var toolName = NormalizeCommand(rawCommand);
        var workerHostPipeName = GetOption(args, "--pipe") ?? BridgeConstants.DefaultWorkerHostPipeName;
        var kernelPipeName = GetOption(args, "--kernel-pipe") ?? BridgeConstants.DefaultKernelPipeName;
        var legacyPipeName = GetOption(args, "--legacy-pipe") ?? BridgeConstants.DefaultPipeName;
        var allowKernelFallback = HasArg(args, "--allow-kernel-fallback") || HasArg(args, "--allow-transitional-fallback");
        var allowLegacyFallback = HasArg(args, "--allow-legacy-fallback") || HasArg(args, "--allow-transitional-fallback");
        var payloadArg = GetOption(args, "--payload");
        var payloadJson = ResolvePayload(toolName, payloadArg);
        var targetDocument = GetOption(args, "--target-document") ?? string.Empty;
        var targetView = GetOption(args, "--target-view") ?? string.Empty;
        var approvalToken = GetOption(args, "--approval-token") ?? string.Empty;
        var previewRunId = GetOption(args, "--preview-run-id") ?? string.Empty;
        var expectedContext = ResolveJsonLikeOption(GetOption(args, "--expected-context"), "--expected-context");
        var scopeJson = ResolveJsonLikeOption(GetOption(args, "--scope"), "--scope");
        var sessionId = GetOption(args, "--session-id") ?? "cli-session";
        var correlationId = GetOption(args, "--correlation-id") ?? Guid.NewGuid().ToString("N");
        var dryRunOverride = GetOption(args, "--dry-run");
        var dryRun = string.IsNullOrWhiteSpace(dryRunOverride) || bool.Parse(dryRunOverride);

        var request = new ToolRequestEnvelope
        {
            RequestId = Guid.NewGuid().ToString("N"),
            ToolName = toolName,
            PayloadJson = payloadJson,
            Caller = "BIM765T.Revit.Bridge CLI",
            SessionId = sessionId,
            DryRun = dryRun,
            TargetDocument = targetDocument,
            TargetView = targetView,
            ExpectedContextJson = expectedContext,
            ApprovalToken = approvalToken,
            ScopeDescriptorJson = scopeJson,
            PreviewRunId = previewRunId,
            CorrelationId = correlationId,
            ProtocolVersion = BridgeProtocol.PipeV1
        };

        try
        {
            ToolResponseEnvelope response;
            try
            {
                response = await InvokeViaWorkerHostAsync(request, workerHostPipeName).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is TimeoutException || ex is IOException || ex is SocketException || ex is OperationCanceledException || ex is Grpc.Core.RpcException)
            {
                if (allowKernelFallback)
                {
                    try
                    {
                        response = await InvokeViaKernelPipeAsync(request, kernelPipeName, ex).ConfigureAwait(false);
                    }
                    catch (Exception kernelEx) when (kernelEx is TimeoutException || kernelEx is IOException || kernelEx is SocketException || kernelEx is OperationCanceledException || kernelEx is InvalidProtocolBufferException)
                    {
                        if (!allowLegacyFallback)
                        {
                            throw;
                        }

                        response = await InvokeViaLegacyPipeAsync(request, legacyPipeName, kernelEx).ConfigureAwait(false);
                    }
                }
                else if (allowLegacyFallback)
                {
                    response = await InvokeViaLegacyPipeAsync(request, legacyPipeName, ex).ConfigureAwait(false);
                }
                else
                {
                    throw;
                }
            }

            response.Diagnostics ??= new System.Collections.Generic.List<DiagnosticRecord>();
            response.Diagnostics.Insert(0, DiagnosticRecord.Create(
                "BRIDGE_CANONICAL_PUBLIC_INGRESS",
                DiagnosticSeverity.Info,
                "Canonical public ingress is WorkerHost. Kernel and legacy pipes are transitional adapter lanes only."));

            if (HasDiagnostic(response, "BRIDGE_FALLBACK_KERNEL_PIPE"))
            {
                response.Diagnostics.Insert(1, DiagnosticRecord.Create(
                    "BRIDGE_TOPOLOGY_TRANSITIONAL",
                    DiagnosticSeverity.Warning,
                    "CLI bridge fell back to the private Revit kernel pipe. This path is transitional; WorkerHost remains the canonical public ingress."));
            }

            if (HasDiagnostic(response, "BRIDGE_FALLBACK_LEGACY_PIPE"))
            {
                response.Diagnostics.Insert(1, DiagnosticRecord.Create(
                    "BRIDGE_TOPOLOGY_LEGACY",
                    DiagnosticSeverity.Warning,
                    "CLI bridge fell back to the legacy pipe. This lane is legacy and should be considered sunset-bound, not canonical."));
            }

            if (!HasDiagnostic(response, "BRIDGE_FALLBACK_KERNEL_PIPE") && !HasDiagnostic(response, "BRIDGE_FALLBACK_LEGACY_PIPE"))
            {
                response.Diagnostics.Insert(1, DiagnosticRecord.Create(
                    "BRIDGE_TOPOLOGY_CANONICAL",
                    DiagnosticSeverity.Info,
                    "CLI bridge is operating through the canonical WorkerHost public ingress."));
            }

            Console.WriteLine(JsonUtil.Serialize(response));
            return response.Succeeded ? 0 : 1;
        }
        catch (Exception ex) when (ex is TimeoutException || ex is IOException || ex is SocketException || ex is Grpc.Core.RpcException || ex is InvalidProtocolBufferException || ex is JsonException)
        {
            var response = new ToolResponseEnvelope
            {
                RequestId = request.RequestId,
                ToolName = request.ToolName,
                CorrelationId = request.CorrelationId,
                ProtocolVersion = request.ProtocolVersion,
                Succeeded = false,
                StatusCode = StatusCodes.BridgeUnavailable,
                Diagnostics = new System.Collections.Generic.List<DiagnosticRecord>
                {
                    DiagnosticRecord.Create("BRIDGE_CONNECT_FAILED", DiagnosticSeverity.Error, ex.Message)
                }
            };
            Console.WriteLine(JsonUtil.Serialize(response));
            return 1;
        }
        catch (InvalidOperationException ex)
        {
            var response = new ToolResponseEnvelope
            {
                RequestId = request.RequestId,
                ToolName = request.ToolName,
                CorrelationId = request.CorrelationId,
                ProtocolVersion = request.ProtocolVersion,
                Succeeded = false,
                StatusCode = StatusCodes.InvalidRequest,
                Diagnostics = new System.Collections.Generic.List<DiagnosticRecord>
                {
                    DiagnosticRecord.Create("BRIDGE_CLI_INVALID_INPUT", DiagnosticSeverity.Error, ex.Message)
                }
            };
            Console.WriteLine(JsonUtil.Serialize(response));
            return 1;
        }
    }

    private static bool HasDiagnostic(ToolResponseEnvelope response, string code)
    {
        return response.Diagnostics?.Any(d => string.Equals(d.Code, code, StringComparison.OrdinalIgnoreCase)) == true;
    }

    private static async Task<ToolResponseEnvelope> InvokeViaWorkerHostAsync(ToolRequestEnvelope request, string pipeName)
    {
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(BridgeConstants.DefaultPipeConnectTimeoutMs));
        using var channel = CreateWorkerHostChannel(pipeName);
        var client = new CompatibilityService.CompatibilityServiceClient(channel);
        var compatResponse = await client.InvokeToolAsync(BuildCompatRequest(request), cancellationToken: timeoutCts.Token).ResponseAsync.ConfigureAwait(false);
        return new ToolResponseEnvelope
        {
            RequestId = request.RequestId,
            ToolName = compatResponse.ToolName,
            CorrelationId = request.CorrelationId,
            ProtocolVersion = string.IsNullOrWhiteSpace(compatResponse.ProtocolVersion) ? request.ProtocolVersion : compatResponse.ProtocolVersion,
            Succeeded = compatResponse.Status?.Succeeded ?? false,
            StatusCode = compatResponse.Status?.StatusCode ?? StatusCodes.BridgeUnavailable,
            PayloadJson = compatResponse.PayloadJson ?? string.Empty,
            ApprovalToken = compatResponse.ApprovalToken ?? string.Empty,
            PreviewRunId = compatResponse.PreviewRunId ?? string.Empty,
            DiffSummaryJson = compatResponse.DiffSummaryJson ?? string.Empty,
            ReviewSummaryJson = compatResponse.ReviewSummaryJson ?? string.Empty,
            ConfirmationRequired = compatResponse.ConfirmationRequired,
            ChangedIds = compatResponse.ChangedIds.ToList(),
            Artifacts = compatResponse.Artifacts.ToList(),
            Diagnostics = compatResponse.Status?.Diagnostics.Select(x => DiagnosticRecord.Create("WORKERHOST", DiagnosticSeverity.Info, x)).ToList()
                ?? new System.Collections.Generic.List<DiagnosticRecord>()
        };
    }

    private static async Task<ToolResponseEnvelope> InvokeViaLegacyPipeAsync(
        ToolRequestEnvelope request,
        string legacyPipeName,
        Exception workerHostException)
    {
        using var client = new NamedPipeClientStream(
            ".",
            legacyPipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous,
            System.Security.Principal.TokenImpersonationLevel.Impersonation);

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(BridgeConstants.DefaultPipeConnectTimeoutMs));
        await client.ConnectAsync(timeoutCts.Token).ConfigureAwait(false);

        using var reader = new StreamReader(client, Utf8NoBom, false, BridgeConstants.PipeBufferSize, true);
        using var writer = new StreamWriter(client, Utf8NoBom, BridgeConstants.PipeBufferSize, true) { AutoFlush = true };

        await writer.WriteLineAsync(JsonUtil.Serialize(request)).ConfigureAwait(false);
        var responseLine = await reader.ReadLineAsync().ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(responseLine))
        {
            throw new IOException("Legacy pipe returned empty response.");
        }

        var response = JsonUtil.Deserialize<ToolResponseEnvelope>(responseLine);
        response.Diagnostics ??= new System.Collections.Generic.List<DiagnosticRecord>();
        response.Diagnostics.Insert(0, DiagnosticRecord.Create("BRIDGE_FALLBACK_LEGACY_PIPE", DiagnosticSeverity.Warning, workerHostException.Message));
        if (string.IsNullOrWhiteSpace(response.ProtocolVersion))
        {
            response.ProtocolVersion = request.ProtocolVersion;
        }

        return response;
    }

    private static async Task<ToolResponseEnvelope> InvokeViaKernelPipeAsync(
        ToolRequestEnvelope request,
        string kernelPipeName,
        Exception workerHostException)
    {
        using var client = new NamedPipeClientStream(
            ".",
            kernelPipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous,
            System.Security.Principal.TokenImpersonationLevel.Impersonation);

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(BridgeConstants.DefaultPipeConnectTimeoutMs));
        await client.ConnectAsync(timeoutCts.Token).ConfigureAwait(false);

        var kernelRequest = new KernelInvokeRequest
        {
            RequestId = request.RequestId ?? string.Empty,
            ToolName = request.ToolName ?? string.Empty,
            PayloadJson = request.PayloadJson ?? string.Empty,
            Caller = request.Caller ?? string.Empty,
            SessionId = request.SessionId ?? string.Empty,
            DryRun = request.DryRun,
            TargetDocument = request.TargetDocument ?? string.Empty,
            TargetView = request.TargetView ?? string.Empty,
            ExpectedContextJson = request.ExpectedContextJson ?? string.Empty,
            ApprovalToken = request.ApprovalToken ?? string.Empty,
            ScopeDescriptorJson = request.ScopeDescriptorJson ?? string.Empty,
            PreviewRunId = request.PreviewRunId ?? string.Empty,
            CorrelationId = request.CorrelationId ?? string.Empty,
            TimeoutMs = BridgeConstants.DefaultRequestTimeoutSeconds * 1000
        };

        await Task.Run(() => kernelRequest.WriteDelimitedTo(client), timeoutCts.Token).ConfigureAwait(false);
        await client.FlushAsync(timeoutCts.Token).ConfigureAwait(false);

        var kernelResponse = await Task.Run(() => KernelInvokeResponse.Parser.ParseDelimitedFrom(client), timeoutCts.Token).ConfigureAwait(false)
            ?? new KernelInvokeResponse();
        var response = new ToolResponseEnvelope
        {
            RequestId = string.IsNullOrWhiteSpace(kernelResponse.RequestId) ? request.RequestId ?? string.Empty : kernelResponse.RequestId,
            ToolName = string.IsNullOrWhiteSpace(kernelResponse.ToolName) ? request.ToolName ?? string.Empty : kernelResponse.ToolName,
            CorrelationId = string.IsNullOrWhiteSpace(kernelResponse.CorrelationId) ? request.CorrelationId ?? string.Empty : kernelResponse.CorrelationId,
            ProtocolVersion = string.IsNullOrWhiteSpace(kernelResponse.ProtocolVersion) ? request.ProtocolVersion ?? string.Empty : kernelResponse.ProtocolVersion,
            Succeeded = kernelResponse.Succeeded,
            StatusCode = kernelResponse.StatusCode ?? string.Empty,
            PayloadJson = kernelResponse.PayloadJson ?? string.Empty,
            ApprovalToken = kernelResponse.ApprovalToken ?? string.Empty,
            PreviewRunId = kernelResponse.PreviewRunId ?? string.Empty,
            DiffSummaryJson = kernelResponse.DiffSummaryJson ?? string.Empty,
            ReviewSummaryJson = kernelResponse.ReviewSummaryJson ?? string.Empty,
            ConfirmationRequired = kernelResponse.ConfirmationRequired,
            DurationMs = kernelResponse.DurationMs
        };

        response.Diagnostics ??= new System.Collections.Generic.List<DiagnosticRecord>();
        response.Diagnostics.Add(DiagnosticRecord.Create("BRIDGE_FALLBACK_KERNEL_PIPE", DiagnosticSeverity.Warning, workerHostException.Message));
        response.Diagnostics.AddRange(kernelResponse.Diagnostics.Select(message => DiagnosticRecord.Create("KERNEL_PIPE", DiagnosticSeverity.Info, message)));
        response.ChangedIds = kernelResponse.ChangedIds.ToList();
        response.Artifacts = kernelResponse.Artifacts.ToList();

        return response;
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

    private static string NormalizeCommand(string rawCommand)
    {
        if (string.Equals(rawCommand, "tools", StringComparison.OrdinalIgnoreCase))
        {
            return ToolNames.SessionListTools;
        }

        if (string.Equals(rawCommand, "capabilities", StringComparison.OrdinalIgnoreCase))
        {
            return ToolNames.SessionGetCapabilities;
        }

        return rawCommand;
    }

    private static string? GetOption(string[] args, string key)
    {
        var idx = Array.FindIndex(args, x => string.Equals(x, key, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0 && idx < args.Length - 1)
        {
            return args[idx + 1];
        }

        return null;
    }

    private static bool HasArg(string[] args, string key)
    {
        return Array.Exists(args, x => string.Equals(x, key, StringComparison.OrdinalIgnoreCase));
    }

    private static string ResolvePayload(string toolName, string? payloadArg)
    {
        if (!string.IsNullOrWhiteSpace(payloadArg))
        {
            return CliFileInputGuard.ResolveJsonOrFile(payloadArg, "--payload");
        }

        if (string.Equals(toolName, ToolNames.ElementQuery, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(toolName, ToolNames.ElementInspect, StringComparison.OrdinalIgnoreCase))
        {
            return JsonUtil.Serialize(new ElementQueryRequest());
        }

        if (string.Equals(toolName, ToolNames.ElementExplain, StringComparison.OrdinalIgnoreCase))
        {
            return JsonUtil.Serialize(new ElementExplainRequest());
        }

        if (string.Equals(toolName, ToolNames.ElementGraph, StringComparison.OrdinalIgnoreCase))
        {
            return JsonUtil.Serialize(new ElementGraphRequest());
        }

        if (string.Equals(toolName, ToolNames.SessionListTools, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(toolName, ToolNames.SessionGetCapabilities, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(toolName, ToolNames.TypeListElementTypes, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(toolName, ToolNames.AnnotationListTextNoteTypes, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(toolName, ToolNames.DocumentGetActive, StringComparison.OrdinalIgnoreCase))
        {
            return "{}";
        }

        return "{}";
    }

    private static string ResolveJsonLikeOption(string? raw, string optionName)
    {
        return string.IsNullOrWhiteSpace(raw)
            ? string.Empty
            : CliFileInputGuard.ResolveJsonOrFile(raw, optionName);
    }
}
