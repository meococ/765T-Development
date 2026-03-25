using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using BIM765T.Revit.Contracts.Proto;
using Google.Protobuf;
using BridgeProtocol = BIM765T.Revit.Contracts.Common.BridgeProtocol;
using StatusCodes = BIM765T.Revit.Contracts.Common.StatusCodes;

namespace BIM765T.Revit.WorkerHost.Kernel;

internal sealed class KernelPipeClient : IKernelClient
{
    private const int DefaultTimeoutMs = 120_000;
    private const int InitialConnectTimeoutMs = 10_000; // Increased from 5s to 10s for Revit startup
    private const int MaxConnectTimeoutMs = 30_000;
    private const int MaxRetries = 3;
    private readonly string _pipeName;

    public KernelPipeClient(string pipeName)
    {
        _pipeName = pipeName;
    }

    public async Task<KernelInvocationResult> InvokeAsync(KernelToolRequest request, CancellationToken cancellationToken)
    {
        var timeoutMs = request.TimeoutMs > 0 ? request.TimeoutMs : DefaultTimeoutMs;
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeoutMs);
        var effectiveToken = timeoutCts.Token;

        // Retry loop for connection with exponential backoff
        var connectTimeoutMs = InitialConnectTimeoutMs;
        Exception? lastException = null;
        for (var attempt = 0; attempt < MaxRetries; attempt++)
        {
            if (TryCheckPipeAdvertised(_pipeName, out var pipeAdvertised) && !pipeAdvertised)
            {
                if (attempt < MaxRetries - 1)
                {
                    var delay = ComputeBackoffDelayMs(attempt);
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                return BuildFailure(StatusCodes.RevitUnavailable, "Revit kernel pipe unavailable. Open Revit with an active project and try again.");
            }

            using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Impersonation);
            try
            {
                using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(effectiveToken);
                connectCts.CancelAfter(Math.Min(timeoutMs, connectTimeoutMs));
                await client.ConnectAsync(connectCts.Token).ConfigureAwait(false);
                return await SendRequestAsync(client, request, effectiveToken, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && effectiveToken.IsCancellationRequested)
            {
                return BuildFailure(StatusCodes.Timeout, "Kernel request timed out before Revit became available.");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && !effectiveToken.IsCancellationRequested)
            {
                // If overall timeout hasn't fired, this was a connect timeout - retry
                if (attempt < MaxRetries - 1)
                {
                    var delay = ComputeBackoffDelayMs(attempt);
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                    connectTimeoutMs = Math.Min(connectTimeoutMs * 2, MaxConnectTimeoutMs);
                    lastException = null;
                    continue;
                }
                return BuildFailure(StatusCodes.RevitUnavailable, "Revit kernel pipe unavailable after multiple connection attempts. Open Revit with an active project and try again.");
            }
            catch (IOException ex) when (attempt < MaxRetries - 1)
            {
                lastException = ex;
                var delay = ComputeBackoffDelayMs(attempt);
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                connectTimeoutMs = Math.Min(connectTimeoutMs * 2, MaxConnectTimeoutMs);
                continue;
            }
            catch (TimeoutException ex) when (attempt < MaxRetries - 1)
            {
                lastException = ex;
                var delay = ComputeBackoffDelayMs(attempt);
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                connectTimeoutMs = Math.Min(connectTimeoutMs * 2, MaxConnectTimeoutMs);
                continue;
            }
            catch (Exception ex) when (attempt < MaxRetries - 1)
            {
                lastException = ex;
                var delay = ComputeBackoffDelayMs(attempt);
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                connectTimeoutMs = Math.Min(connectTimeoutMs * 2, MaxConnectTimeoutMs);
                continue;
            }
        }

        return BuildFailure(StatusCodes.RevitUnavailable, "Revit kernel pipe unavailable. Open Revit with an active project and try again.", lastException?.Message);
    }

    private static int ComputeBackoffDelayMs(int attempt)
    {
        // Exponential backoff: 500ms, 1000ms, 2000ms
        return 500 * (1 << Math.Min(attempt, 3));
    }

    private static bool TryCheckPipeAdvertised(string pipeName, out bool advertised)
    {
        advertised = true;

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return false;
        }

        try
        {
            advertised = WaitNamedPipe($@"\\.\pipe\{pipeName}", 0);
            if (advertised)
            {
                return true;
            }

            var lastError = Marshal.GetLastWin32Error();
            if (lastError == ErrorFileNotFound)
            {
                advertised = false;
                return true;
            }

            if (lastError == ErrorSemTimeout || lastError == ErrorPipeBusy)
            {
                advertised = true;
                return true;
            }

            throw new Win32Exception(lastError);
        }
        catch
        {
            advertised = true;
            return false;
        }
    }

    private static async Task<KernelInvocationResult> SendRequestAsync(NamedPipeClientStream client, KernelToolRequest request, CancellationToken effectiveToken, CancellationToken cancellationToken)
    {

        var payload = new KernelInvokeRequest
        {
            CorrelationId = request.CorrelationId ?? string.Empty,
            CausationId = request.CausationId ?? string.Empty,
            MissionId = request.MissionId ?? string.Empty,
            ActorId = request.ActorId ?? string.Empty,
            DocumentKey = request.DocumentKey ?? string.Empty,
            RequestedAtUtc = request.RequestedAtUtc ?? string.Empty,
            TimeoutMs = request.TimeoutMs,
            CancellationTokenId = request.CancellationTokenId ?? string.Empty,
            RequestId = Guid.NewGuid().ToString("N"),
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
            PreviewRunId = request.PreviewRunId ?? string.Empty
        };

        try
        {
            payload.WriteDelimitedTo(client);
            await client.FlushAsync(effectiveToken).ConfigureAwait(false);

            var response = await Task.Run(() => KernelInvokeResponse.Parser.ParseDelimitedFrom(client), effectiveToken).ConfigureAwait(false);
            return new KernelInvocationResult
            {
                Succeeded = response.Succeeded,
                StatusCode = response.StatusCode,
                PayloadJson = response.PayloadJson,
                ApprovalToken = response.ApprovalToken,
                PreviewRunId = response.PreviewRunId,
                DiffSummaryJson = response.DiffSummaryJson,
                ReviewSummaryJson = response.ReviewSummaryJson,
                ConfirmationRequired = response.ConfirmationRequired,
                Diagnostics = response.Diagnostics.ToList(),
                ChangedIds = response.ChangedIds.ToList(),
                Artifacts = response.Artifacts.ToList(),
                ProtocolVersion = response.ProtocolVersion
            };
        }
        catch (OperationCanceledException) when (!effectiveToken.IsCancellationRequested)
        {
            return BuildFailure(StatusCodes.Timeout, "Kernel request timed out before Revit returned a response.");
        }
        catch (IOException ex)
        {
            return BuildFailure(StatusCodes.RevitUnavailable, "Revit kernel pipe disconnected while processing the request.", ex.Message);
        }
    }

    private static KernelInvocationResult BuildFailure(string statusCode, string summary, string? detail = null)
    {
        var diagnostics = new List<string> { summary };
        if (!string.IsNullOrWhiteSpace(detail) && !string.Equals(detail, summary, StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(detail);
        }

        return new KernelInvocationResult
        {
            Succeeded = false,
            StatusCode = statusCode,
            PayloadJson = string.Empty,
            Diagnostics = diagnostics,
            ProtocolVersion = BridgeProtocol.PipeV1
        };
    }

    private const int ErrorFileNotFound = 2;
    private const int ErrorSemTimeout = 121;
    private const int ErrorPipeBusy = 231;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool WaitNamedPipe(string name, int timeout);
}
