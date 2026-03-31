using System;
using System.Collections.Generic;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using BIM765T.Revit.Agent.Config;
using BIM765T.Revit.Agent.Infrastructure.Logging;
using BIM765T.Revit.Agent.Services.Bridge;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;
using BIM765T.Revit.Contracts.Serialization;

namespace BIM765T.Revit.Agent.Infrastructure.Bridge;

internal interface IPipeCallerAuthorizer
{
    bool TryAuthorize(string clientIdentity, bool lookupFailed, out string rejectReason);
}

internal interface IPipeRequestScheduler
{
    Task<ToolResponseEnvelope> ScheduleAsync(ToolRequestEnvelope request, int timeoutMs, CancellationToken token);
}

internal sealed class PipeRequestProcessor
{
    private readonly AgentSettings _settings;
    private readonly Func<string, ToolManifest?> _manifestResolver;
    private readonly RequestRateLimiter _rateLimiter;
    private readonly IAgentLogger _logger;
    private readonly IPipeCallerAuthorizer _callerAuthorizer;
    private readonly IPipeRequestScheduler _scheduler;

    internal PipeRequestProcessor(
        AgentSettings settings,
        Func<string, ToolManifest?> manifestResolver,
        RequestRateLimiter rateLimiter,
        IAgentLogger logger,
        IPipeCallerAuthorizer callerAuthorizer,
        IPipeRequestScheduler scheduler)
    {
        _settings = settings;
        _manifestResolver = manifestResolver;
        _rateLimiter = rateLimiter;
        _logger = logger;
        _callerAuthorizer = callerAuthorizer;
        _scheduler = scheduler;
    }

    internal async Task<ToolResponseEnvelope> ProcessAsync(string line, string clientIdentity, bool lookupFailed, CancellationToken token)
    {
        ToolRequestEnvelope request;
        try
        {
            request = JsonUtil.DeserializeRequired<ToolRequestEnvelope>(line);
        }
        catch (Exception ex)
        {
            return new ToolResponseEnvelope
            {
                Succeeded = false,
                StatusCode = StatusCodes.InvalidRequest,
                ProtocolVersion = BridgeProtocol.PipeV1,
                Diagnostics = new List<DiagnosticRecord>
                {
                    DiagnosticRecord.Create("INVALID_JSON", DiagnosticSeverity.Error, ex.Message)
                }
            };
        }

        request.ProtocolVersion = BridgeProtocol.NormalizeOrDefault(request.ProtocolVersion);
        if (!BridgeProtocol.IsSupported(request.ProtocolVersion))
        {
            return ToolResponses.Failure(
                request,
                StatusCodes.ProtocolUnsupported,
                DiagnosticRecord.Create(
                    "PROTOCOL_UNSUPPORTED",
                    DiagnosticSeverity.Error,
                    $"Bridge pipe protocol không hỗ trợ phiên bản `{request.ProtocolVersion}`. Supported={BridgeProtocol.PipeV1}."));
        }

        if (string.IsNullOrWhiteSpace(request.CorrelationId))
        {
            request.CorrelationId = request.RequestId;
        }

        using var logScope = _logger.BeginScope(request.CorrelationId, request.ToolName, "pipe");
        _logger.Info("Received pipe request.");

        if (!string.IsNullOrWhiteSpace(clientIdentity))
        {
            request.Caller = string.IsNullOrWhiteSpace(request.Caller)
                ? $"pipe:{clientIdentity}"
                : $"{request.Caller} | pipe:{clientIdentity}";
        }

        if (!_callerAuthorizer.TryAuthorize(clientIdentity, lookupFailed, out var rejectReason))
        {
            _logger.Warn("Pipe caller rejected: " + rejectReason);
            return ToolResponses.Failure(
                request,
                StatusCodes.CallerNotAllowed,
                DiagnosticRecord.Create("PIPE_CALLER_REJECTED", DiagnosticSeverity.Error, $"Named pipe caller không được phép: {rejectReason}"));
        }

        var manifest = _manifestResolver(request.ToolName);
        if (!IsManifestAllowedForCaller(request.Caller, manifest, out var callerPolicyReason))
        {
            _logger.Warn("Pipe caller blocked by manifest exposure policy: " + callerPolicyReason);
            return ToolResponses.Failure(
                request,
                StatusCodes.CallerNotAllowed,
                DiagnosticRecord.Create("PIPE_CALLER_POLICY_BLOCKED", DiagnosticSeverity.Error, callerPolicyReason));
        }

        var rateLimitDecision = _rateLimiter.Evaluate(request.Caller, manifest);
        if (!rateLimitDecision.Allowed)
        {
            _logger.Warn($"Pipe caller rate limited: {request.Caller} | Tool={request.ToolName} | RetryAfter={rateLimitDecision.RetryAfter.TotalSeconds:F1}s");
            return ToolResponses.Failure(
                request,
                StatusCodes.RateLimited,
                DiagnosticRecord.Create(
                    "RATE_LIMITED",
                    DiagnosticSeverity.Error,
                    $"Rate limit exceeded for caller '{request.Caller}'. Limit={rateLimitDecision.Limit}/{rateLimitDecision.WindowSeconds}s, retry after {Math.Ceiling(rateLimitDecision.RetryAfter.TotalSeconds)}s."));
        }

        var timeoutMs = ToolExecutionTimeoutPolicy.ResolveExecutionTimeoutMs(_settings, manifest);
        var response = await _scheduler.ScheduleAsync(request, timeoutMs, token).ConfigureAwait(false);
        response.ProtocolVersion = BridgeProtocol.NormalizeOrDefault(response.ProtocolVersion);
        _logger.Info("Responding pipe request with status " + response.StatusCode + ".");
        return response;
    }

    private static bool IsManifestAllowedForCaller(string caller, ToolManifest? manifest, out string reason)
    {
        reason = string.Empty;
        if (manifest == null)
        {
            return true;
        }

        var normalizedCaller = caller ?? string.Empty;
        var isMcpCaller = normalizedCaller.IndexOf("BIM765T.Revit.McpHost", StringComparison.OrdinalIgnoreCase) >= 0;
        if (!isMcpCaller)
        {
            return true;
        }

        if (string.Equals(manifest.Visibility, WorkerVisibility.Hidden, StringComparison.OrdinalIgnoreCase)
            || string.Equals(manifest.Visibility, WorkerVisibility.BetaInternal, StringComparison.OrdinalIgnoreCase)
            || string.Equals(manifest.Audience, WorkerAudience.Internal, StringComparison.OrdinalIgnoreCase)
            || string.Equals(manifest.PrimaryPersona, ToolPrimaryPersonas.PlatformAuthor, StringComparison.OrdinalIgnoreCase))
        {
            reason = $"MCP caller cannot invoke tool '{manifest.ToolName}' because it is not exposed on the MCP surface.";
            return false;
        }

        if (!string.Equals(manifest.Audience, WorkerAudience.Commercial, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(manifest.Audience, WorkerAudience.Connector, StringComparison.OrdinalIgnoreCase))
        {
            reason = $"MCP caller cannot invoke tool '{manifest.ToolName}' because audience '{manifest.Audience}' is not allowed on the MCP surface.";
            return false;
        }

        return true;
    }
}

internal sealed class WindowsPipeCallerAuthorizer : IPipeCallerAuthorizer
{
    public bool TryAuthorize(string clientIdentity, bool lookupFailed, out string rejectReason)
    {
        if (lookupFailed || string.IsNullOrWhiteSpace(clientIdentity))
        {
            rejectReason = lookupFailed
                ? "identity lookup failed (GetImpersonationUserName threw); client cần TokenImpersonationLevel.Impersonation"
                : "empty identity";
            return false;
        }

        var currentIdentity = WindowsIdentity.GetCurrent();
        var currentSid = currentIdentity.User;

        try
        {
            SecurityIdentifier? callerSid;
            try
            {
                var account = new NTAccount(clientIdentity);
                callerSid = (SecurityIdentifier)account.Translate(typeof(SecurityIdentifier));
            }
            catch
            {
                callerSid = null;
            }

            if (callerSid != null && currentSid != null)
            {
                if (callerSid.Equals(currentSid))
                {
                    rejectReason = string.Empty;
                    return true;
                }

                rejectReason = $"SID mismatch: caller SID {callerSid.Value} != current SID {currentSid.Value}";
                return false;
            }

            var current = currentIdentity.Name ?? string.Empty;
            if (string.Equals(clientIdentity, current, StringComparison.OrdinalIgnoreCase))
            {
                rejectReason = string.Empty;
                return true;
            }

            var backslash = current.IndexOf('\\');
            if (backslash >= 0)
            {
                var shortName = current.Substring(backslash + 1);
                if (string.Equals(clientIdentity, shortName, StringComparison.OrdinalIgnoreCase))
                {
                    rejectReason = string.Empty;
                    return true;
                }
            }

            rejectReason = $"caller identity '{clientIdentity}' != current identity '{current}'";
            return false;
        }
        catch (Exception ex)
        {
            rejectReason = ex.Message;
            return false;
        }
    }
}
