using System;
using System.Collections.Generic;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using BIM765T.Revit.Agent.Config;
using BIM765T.Revit.Agent.Infrastructure.Logging;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Proto;
using Google.Protobuf;

namespace BIM765T.Revit.Agent.Infrastructure.Bridge;

internal sealed class KernelPipeHostedService : IDisposable
{
    private readonly AgentSettings _settings;
    private readonly IAgentLogger _logger;
    private readonly IPipeRequestScheduler _scheduler;
    private readonly IPipeCallerAuthorizer _authorizer;
    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    internal KernelPipeHostedService(
        AgentSettings settings,
        IAgentLogger logger,
        IPipeRequestScheduler scheduler,
        IPipeCallerAuthorizer authorizer)
    {
        _settings = settings;
        _logger = logger;
        _scheduler = scheduler;
        _authorizer = authorizer;
    }

    internal void Start()
    {
        if (_cts != null || !_settings.EnableKernelPipeServer)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        _loopTask = Task.Run(() => AcceptLoopAsync(_cts.Token));
        _logger.Info("Kernel pipe server started: " + _settings.KernelPipeName);
    }

    private async Task AcceptLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;
            try
            {
                server = new NamedPipeServerStream(
                    _settings.KernelPipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous,
                    BridgeConstants.PipeBufferSize,
                    BridgeConstants.PipeBufferSize,
                    CreatePipeSecurity());

                await Task.Factory.FromAsync(server.BeginWaitForConnection, server.EndWaitForConnection, null).ConfigureAwait(false);
                var connectedServer = server;
                _ = Task.Run(() => HandleConnectionAsync(connectedServer, token), token);
                server = null;
            }
            catch (ObjectDisposedException)
            {
                server?.Dispose();
                break;
            }
            catch (Exception ex)
            {
                server?.Dispose();
                _logger.Error("Kernel pipe server loop failure.", ex);
                await Task.Delay(500, token).ConfigureAwait(false);
            }
        }
    }

    private const int PipeReadTimeoutSeconds = 30;

    private async Task HandleConnectionAsync(NamedPipeServerStream server, CancellationToken token)
    {
        try
        {
            if (server == null)
            {
                _logger.Error("Kernel pipe connection failed.", new ArgumentNullException(nameof(server)));
                return;
            }

            using (server)
            {
                var clientIdentity = TryGetClientIdentity(server, out var lookupFailed);
                if (!_authorizer.TryAuthorize(clientIdentity, lookupFailed, out var rejectReason))
                {
                    var rejectResponse = new KernelInvokeResponse
                    {
                        Succeeded = false,
                        StatusCode = StatusCodes.CallerNotAllowed,
                        Diagnostics = { $"Kernel pipe caller rejected: {rejectReason}" },
                        ProtocolVersion = BridgeProtocol.PipeV1
                    };
                    rejectResponse.WriteDelimitedTo(server);
                    await server.FlushAsync(token).ConfigureAwait(false);
                    return;
                }

                var request = await ReadWithTimeoutAsync(server, token).ConfigureAwait(false);
                if (request == null)
                {
                    // Timeout or cancellation — pipe already closed by ReadWithTimeoutAsync.
                    return;
                }
                var toolRequest = new ToolRequestEnvelope
                {
                    RequestId = string.IsNullOrWhiteSpace(request.RequestId) ? Guid.NewGuid().ToString("N") : request.RequestId,
                    ToolName = request.ToolName ?? string.Empty,
                    PayloadJson = request.PayloadJson ?? string.Empty,
                    Caller = string.IsNullOrWhiteSpace(request.Caller) ? $"kernel:{clientIdentity}" : request.Caller,
                    SessionId = request.SessionId ?? string.Empty,
                    DryRun = request.DryRun,
                    TargetDocument = request.TargetDocument ?? string.Empty,
                    TargetView = request.TargetView ?? string.Empty,
                    ExpectedContextJson = request.ExpectedContextJson ?? string.Empty,
                    ApprovalToken = request.ApprovalToken ?? string.Empty,
                    ScopeDescriptorJson = request.ScopeDescriptorJson ?? string.Empty,
                    PreviewRunId = request.PreviewRunId ?? string.Empty,
                    CorrelationId = request.CorrelationId ?? string.Empty,
                    ProtocolVersion = BridgeProtocol.PipeV1
                };

                var timeoutMs = request.TimeoutMs > 0 ? request.TimeoutMs : _settings.RequestTimeoutSeconds * 1000;
                var response = await _scheduler.ScheduleAsync(toolRequest, timeoutMs, token).ConfigureAwait(false);
                var kernelResponse = new KernelInvokeResponse
                {
                    RequestId = response.RequestId ?? string.Empty,
                    ToolName = response.ToolName ?? string.Empty,
                    Succeeded = response.Succeeded,
                    StatusCode = response.StatusCode ?? string.Empty,
                    PayloadJson = response.PayloadJson ?? string.Empty,
                    ConfirmationRequired = response.ConfirmationRequired,
                    ApprovalToken = response.ApprovalToken ?? string.Empty,
                    DiffSummaryJson = response.DiffSummaryJson ?? string.Empty,
                    ReviewSummaryJson = response.ReviewSummaryJson ?? string.Empty,
                    DurationMs = response.DurationMs,
                    PreviewRunId = response.PreviewRunId ?? string.Empty,
                    CorrelationId = response.CorrelationId ?? string.Empty,
                    ProtocolVersion = response.ProtocolVersion ?? string.Empty
                };
                kernelResponse.Diagnostics.AddRange(response.Diagnostics?.ConvertAll(x => x.Code + ": " + x.Message) ?? new List<string>());
                kernelResponse.ChangedIds.AddRange(response.ChangedIds ?? new List<int>());
                kernelResponse.Artifacts.AddRange(response.Artifacts ?? new List<string>());
                kernelResponse.WriteDelimitedTo(server);
                await server.FlushAsync(token).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Kernel pipe connection failed.", ex);
        }
    }

    /// <summary>
    /// Reads a <see cref="KernelInvokeRequest"/> from <paramref name="server"/> with a
    /// <see cref="PipeReadTimeoutSeconds"/>-second hard deadline. If the deadline expires the
    /// pipe is closed so the blocked thread-pool thread unblocks, and <see langword="null"/>
    /// is returned so the caller can skip processing and accept the next connection.
    /// </summary>
    private async Task<KernelInvokeRequest?> ReadWithTimeoutAsync(NamedPipeServerStream server, CancellationToken token)
    {
        using var readTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        readTimeoutCts.CancelAfter(TimeSpan.FromSeconds(PipeReadTimeoutSeconds));

        // Task.WaitAsync is not available on net48; use Task.WhenAny with a delay task instead.
        var readTask = Task.Run(() => KernelInvokeRequest.Parser.ParseDelimitedFrom(server), readTimeoutCts.Token);
        var delayTask = Task.Delay(TimeSpan.FromSeconds(PipeReadTimeoutSeconds), readTimeoutCts.Token);

        var completed = await Task.WhenAny(readTask, delayTask).ConfigureAwait(false);
        if (completed == delayTask || token.IsCancellationRequested)
        {
            if (!token.IsCancellationRequested)
            {
                // Read timeout expired (not a host shutdown) — close the pipe so the
                // blocked ParseDelimitedFrom call in the thread pool unblocks.
                _logger.Warn($"Pipe connection timed out after {PipeReadTimeoutSeconds}s — closing stale connection.");
                try { server.Close(); } catch { /* best-effort */ }
            }

            return null;
        }

        // readTask completed first — propagate any exception (e.g. malformed proto).
        return await readTask.ConfigureAwait(false);
    }

    public void Dispose()
    {
        try
        {
            _cts?.Cancel();
            _loopTask?.Wait(1500);
        }
        catch
        {
            // Ignore shutdown noise.
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            _loopTask = null;
        }
    }

    private static PipeSecurity CreatePipeSecurity()
    {
        var security = new PipeSecurity();
        var sid = WindowsIdentity.GetCurrent().User;
        if (sid != null)
        {
            security.AddAccessRule(new PipeAccessRule(sid, PipeAccessRights.FullControl, AccessControlType.Allow));
        }

        security.SetAccessRuleProtection(true, false);
        return security;
    }

    private static string TryGetClientIdentity(NamedPipeServerStream server, out bool lookupFailed)
    {
        lookupFailed = false;
        try
        {
            return server.GetImpersonationUserName() ?? string.Empty;
        }
        catch
        {
            lookupFailed = true;
            return string.Empty;
        }
    }
}
