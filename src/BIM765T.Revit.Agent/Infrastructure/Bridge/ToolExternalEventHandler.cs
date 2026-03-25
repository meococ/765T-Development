using System;
using Autodesk.Revit.UI;
using BIM765T.Revit.Agent.Infrastructure.Logging;
using BIM765T.Revit.Agent.Infrastructure.Time;
using BIM765T.Revit.Agent.Services.Bridge;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Agent.Infrastructure.Bridge;

internal sealed class ToolExternalEventHandler : IExternalEventHandler
{
    private readonly ToolInvocationQueue _queue;
    private readonly ToolExecutor _executor;
    private readonly IAgentLogger _logger;
    private readonly ISystemClock _clock;
    private readonly object _activeLock = new object();
    private string _activeToolName = string.Empty;
    private string _activeRequestId = string.Empty;
    private DateTime? _activeStartedUtc;
    private DateTime? _activeHeartbeatUtc;
    private string _activeStage = string.Empty;
    private string _activeExecutionTier = WorkerExecutionTiers.Tier0;
    private string _activeRiskTier = ToolRiskTiers.Tier0;
    private string _activeLatencyClass = ToolLatencyClasses.Standard;

    internal ToolExternalEventHandler(ToolInvocationQueue queue, ToolExecutor executor, IAgentLogger logger, ISystemClock clock)
    {
        _queue = queue;
        _executor = executor;
        _logger = logger;
        _clock = clock;
    }

    public void Execute(UIApplication app)
    {
        while (_queue.TryDequeue(out var invocation) && invocation != null)
        {
            using var scope = _logger.BeginScope(invocation.Request.CorrelationId, invocation.Request.ToolName, "external_event");

            if (!invocation.TryBeginExecution())
            {
                if (invocation.IsCancelled)
                {
                    _logger.Info($"Skipped cancelled invocation: {invocation.Request.ToolName} (RequestId: {invocation.Request.RequestId})");
                }

                continue;
            }

            try
            {
                SetActive(invocation, WorkerStages.Execution);
                var response = _executor.Execute(app, invocation.Request);
                response.Stage = string.IsNullOrWhiteSpace(response.Stage) ? WorkerStages.Done : response.Stage;
                response.HeartbeatUtc ??= _clock.UtcNow;
                response.ExecutionTier = string.IsNullOrWhiteSpace(response.ExecutionTier) ? invocation.ExecutionTier : response.ExecutionTier;
                invocation.Completion.TrySetResult(response);
            }
            catch (Exception ex)
            {
                _logger.Error("Tool execution crashed on Revit UI thread.", ex);
                invocation.Completion.TrySetResult(new ToolResponseEnvelope
                {
                    RequestId = invocation.Request.RequestId,
                    ToolName = invocation.Request.ToolName,
                    CorrelationId = invocation.Request.CorrelationId,
                    ProtocolVersion = invocation.Request.ProtocolVersion,
                    Succeeded = false,
                    StatusCode = StatusCodes.InternalError,
                    Stage = WorkerStages.Recovery,
                    HeartbeatUtc = _clock.UtcNow,
                    ExecutionTier = invocation.ExecutionTier,
                    Diagnostics = new System.Collections.Generic.List<DiagnosticRecord>
                    {
                        DiagnosticRecord.Create("UNHANDLED_EXCEPTION", DiagnosticSeverity.Error, ex.Message)
                    }
                });
            }
            finally
            {
                invocation.MarkCompleted();
                ClearActive();
            }
        }
    }

    public string GetName()
    {
        return "BIM765T.Revit.Agent.ToolExternalEventHandler";
    }

    internal QueueStateResponse GetQueueState(ToolInvocationQueue queue)
    {
        lock (_activeLock)
        {
            var heartbeat = _activeHeartbeatUtc;
            if (!string.IsNullOrWhiteSpace(_activeRequestId))
            {
                heartbeat = _clock.UtcNow;
                _activeHeartbeatUtc = heartbeat;
            }

            return new QueueStateResponse
            {
                PendingCount = queue.PendingCount,
                HasActiveInvocation = !string.IsNullOrWhiteSpace(_activeRequestId),
                ActiveToolName = _activeToolName,
                ActiveRequestId = _activeRequestId,
                ActiveStartedUtc = _activeStartedUtc,
                PendingHighPriorityCount = queue.PendingHighPriorityCount,
                PendingNormalPriorityCount = queue.PendingNormalPriorityCount,
                PendingLowPriorityCount = queue.PendingLowPriorityCount,
                ActiveStage = _activeStage,
                ActiveExecutionTier = _activeExecutionTier,
                ActiveRiskTier = _activeRiskTier,
                ActiveLatencyClass = _activeLatencyClass,
                HeartbeatUtc = heartbeat,
                ActiveElapsedMs = _activeStartedUtc.HasValue ? (long)Math.Max(0, (_clock.UtcNow - _activeStartedUtc.Value).TotalMilliseconds) : 0,
                CanCancelPending = queue.PendingCount > 0
            };
        }
    }

    private void SetActive(PendingToolInvocation invocation, string stage)
    {
        lock (_activeLock)
        {
            _activeToolName = invocation.Request.ToolName ?? string.Empty;
            _activeRequestId = invocation.Request.RequestId ?? string.Empty;
            _activeStartedUtc = _clock.UtcNow;
            _activeHeartbeatUtc = _activeStartedUtc;
            _activeStage = stage ?? WorkerStages.Execution;
            _activeExecutionTier = invocation.ExecutionTier;
            _activeRiskTier = invocation.RiskTier;
            _activeLatencyClass = invocation.LatencyClass;
        }
    }

    private void ClearActive()
    {
        lock (_activeLock)
        {
            _activeToolName = string.Empty;
            _activeRequestId = string.Empty;
            _activeStartedUtc = null;
            _activeHeartbeatUtc = null;
            _activeStage = string.Empty;
            _activeExecutionTier = WorkerExecutionTiers.Tier0;
            _activeRiskTier = ToolRiskTiers.Tier0;
            _activeLatencyClass = ToolLatencyClasses.Standard;
        }
    }
}
