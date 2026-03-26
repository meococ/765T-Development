using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Autodesk.Revit.UI;
using BIM765T.Revit.Agent.Infrastructure.Bridge.Workflows;
using BIM765T.Revit.Agent.Infrastructure.Logging;
using BIM765T.Revit.Agent.Infrastructure.Time;
using BIM765T.Revit.Agent.Services.Bridge;
using BIM765T.Revit.Agent.Services.Platform;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Agent.Infrastructure.Bridge;

internal sealed class ToolExternalEventHandler : IExternalEventHandler
{
    /// <summary>
    /// Maximum items to process per idle cycle.
    /// Keeps Revit responsive — remaining items are re-scheduled via <see cref="ExternalEvent.Raise"/>.
    /// </summary>
    private const int MaxItemsPerBatch = 3;

    /// <summary>
    /// Time budget per idle cycle in milliseconds.
    /// ~15 ms ≈ 1 frame at 60 fps — UI stays smooth even under queue pressure.
    /// </summary>
    private const long TimeBudgetMs = 15;

    private readonly ToolInvocationQueue _queue;
    private readonly ToolExecutor _executor;
    private readonly ToolRegistry _registry;
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

    /// <summary>
    /// Set after construction by <see cref="AgentHost"/> so the handler can re-raise itself
    /// when the queue still has items after the time budget expires.
    /// </summary>
    private ExternalEvent? _selfEvent;

    /// <summary>
    /// Workflow continuations that have returned from an async step
    /// and need their next sync step executed on the UI thread.
    /// </summary>
    private readonly ConcurrentQueue<object> _continuations = new ConcurrentQueue<object>();

    internal ToolExternalEventHandler(ToolInvocationQueue queue, ToolExecutor executor, ToolRegistry registry, IAgentLogger logger, ISystemClock clock)
    {
        _queue = queue;
        _executor = executor;
        _registry = registry;
        _logger = logger;
        _clock = clock;
    }

    /// <summary>Wire up after <see cref="ExternalEvent.Create"/> in <see cref="AgentHost"/>.</summary>
    internal void SetSelfEvent(ExternalEvent selfEvent)
    {
        _selfEvent = selfEvent;
    }

    /// <summary>
    /// Enqueue a workflow continuation returned from an async step.
    /// Called from a thread pool thread after <see cref="IAsyncYieldStep{TContext}.ExecuteOffThreadAsync"/> completes.
    /// </summary>
    internal void EnqueueContinuation(object continuation)
    {
        _continuations.Enqueue(continuation);
        _selfEvent?.Raise();
    }

    public void Execute(UIApplication app)
    {
        var sw = Stopwatch.StartNew();
        var processed = 0;

        // ── Phase A: Drive workflow continuations (priority over new queue items) ──
        while (processed < MaxItemsPerBatch
            && sw.ElapsedMilliseconds < TimeBudgetMs
            && _continuations.TryDequeue(out var rawContinuation))
        {
            try
            {
                DriveWorkflowStep(app, rawContinuation);
            }
            catch (Exception ex)
            {
                _logger.Error("Workflow continuation step crashed on UI thread.", ex);
                TryFaultContinuation(rawContinuation, ex);
            }

            processed++;
        }

        // ── Phase B: Process normal tool invocations ──
        while (processed < MaxItemsPerBatch
            && sw.ElapsedMilliseconds < TimeBudgetMs
            && _queue.TryDequeue(out var invocation)
            && invocation != null)
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

            // Workflow-based tools: start multi-step workflow instead of blocking sync handler
            if (_registry.TryGetWorkflow(invocation.Request.ToolName, out var workflow))
            {
                try
                {
                    SetActive(invocation, WorkerStages.Execution);
                    var setup = workflow.Factory(invocation.Request);
                    var continuation = setup.ToContinuation<MessageWorkflowContext>(invocation.Completion, invocation.Request);
                    _continuations.Enqueue(continuation);
                    _selfEvent?.Raise();
                }
                catch (Exception ex)
                {
                    _logger.Error("Workflow factory crashed on Revit UI thread.", ex);
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
                        Diagnostics = new System.Collections.Generic.List<DiagnosticRecord>
                        {
                            DiagnosticRecord.Create("WORKFLOW_FACTORY_EXCEPTION", DiagnosticSeverity.Error, ex.Message)
                        }
                    });
                }
                finally
                {
                    invocation.MarkCompleted();
                    ClearActive();
                    processed++;
                }

                continue;
            }

            // Normal sync tool handler
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
                processed++;
            }
        }

        // Re-schedule if queue or continuations still have items — yields UI thread between batches
        if ((_queue.PendingCount > 0 || !_continuations.IsEmpty) && _selfEvent != null)
        {
            _selfEvent.Raise();
        }
    }

    public string GetName()
    {
        return "BIM765T.Revit.Agent.ToolExternalEventHandler";
    }

    // ── Workflow step machine ───────────────────────────────────────

    /// <summary>Untyped dispatcher for <see cref="DriveWorkflowStep{TContext}"/>.</summary>
    private void DriveWorkflowStep(UIApplication app, object rawContinuation)
    {
        if (rawContinuation is WorkflowContinuation<MessageWorkflowContext> msgContinuation)
        {
            DriveWorkflowStep(app, msgContinuation);
        }
        else if (rawContinuation is WorkflowCompletionMarker<MessageWorkflowContext> marker)
        {
            CompleteContinuation(marker.Inner);
        }
        else
        {
            _logger.Error($"Unknown continuation type: {rawContinuation?.GetType().Name ?? "null"}");
        }
    }

    /// <summary>
    /// Drives one sync step of a workflow. If the step returns an <see cref="IAsyncYieldStep{TContext}"/>,
    /// schedules it on the thread pool and returns (non-blocking). If it returns another sync step,
    /// re-enqueues as continuation for the next cycle. If null, the workflow is complete.
    /// </summary>
    private void DriveWorkflowStep<TContext>(UIApplication app, WorkflowContinuation<TContext> continuation)
    {
        var step = continuation.NextStep;

        if (step is IWorkflowStep<TContext> syncStep)
        {
            object? next;
            try
            {
                next = syncStep.ExecuteOnUIThread(app, continuation.Context);
            }
            catch (Exception ex)
            {
                _logger.Error($"Workflow sync step {syncStep.GetType().Name} faulted.", ex);
                FaultContinuation(continuation, ex);
                return;
            }

            if (next == null)
            {
                CompleteContinuation(continuation);
                return;
            }

            HandleNextStep(continuation, next);
        }
        else if (step is IAsyncYieldStep<TContext> asyncStep)
        {
            ScheduleAsyncStep(continuation, asyncStep);
        }
    }

    private void HandleNextStep<TContext>(WorkflowContinuation<TContext> continuation, object next)
    {
        if (next is IAsyncYieldStep<TContext> asyncStep)
        {
            ScheduleAsyncStep(continuation, asyncStep);
        }
        else if (next is IWorkflowStep<TContext>)
        {
            _continuations.Enqueue(continuation.WithStep(next));
            _selfEvent?.Raise();
        }
        else
        {
            _logger.Error($"Workflow step returned unknown type: {next.GetType().Name}. Completing with error.");
            FaultContinuation(continuation, new InvalidOperationException($"Unknown step type: {next.GetType().Name}"));
        }
    }

    /// <summary>
    /// Fires an async step on the thread pool. When it completes, the returned sync step
    /// is enqueued back as a continuation for the next UI idle cycle.
    /// </summary>
    private void ScheduleAsyncStep<TContext>(WorkflowContinuation<TContext> continuation, IAsyncYieldStep<TContext> asyncStep)
    {
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        Task.Run(async () =>
        {
            try
            {
                var nextSyncStep = await asyncStep.ExecuteOffThreadAsync(continuation.Context, cts.Token).ConfigureAwait(false);
                if (nextSyncStep == null)
                {
                    _continuations.Enqueue(new WorkflowCompletionMarker<TContext>(continuation));
                    _selfEvent?.Raise();
                    return;
                }

                _continuations.Enqueue(continuation.WithStep(nextSyncStep));
                _selfEvent?.Raise();
            }
            catch (Exception ex)
            {
                FaultContinuation(continuation, ex);
            }
            finally
            {
                cts.Dispose();
            }
        });
    }

    private void CompleteContinuation<TContext>(WorkflowContinuation<TContext> continuation)
    {
        if (continuation.Context is MessageWorkflowContext msgCtx && msgCtx.FinalResponse != null)
        {
            var response = ToolResponses.Success(continuation.OriginalRequest, msgCtx.FinalResponse);
            continuation.Completion.TrySetResult(response);
        }
        else
        {
            continuation.Completion.TrySetResult(new ToolResponseEnvelope
            {
                RequestId = continuation.OriginalRequest.RequestId,
                ToolName = continuation.OriginalRequest.ToolName,
                CorrelationId = continuation.OriginalRequest.CorrelationId,
                ProtocolVersion = continuation.OriginalRequest.ProtocolVersion,
                Succeeded = true,
                StatusCode = StatusCodes.Ok,
                Stage = WorkerStages.Done,
                HeartbeatUtc = _clock.UtcNow
            });
        }
    }

    private void FaultContinuation<TContext>(WorkflowContinuation<TContext> continuation, Exception ex)
    {
        continuation.Completion.TrySetResult(new ToolResponseEnvelope
        {
            RequestId = continuation.OriginalRequest.RequestId,
            ToolName = continuation.OriginalRequest.ToolName,
            CorrelationId = continuation.OriginalRequest.CorrelationId,
            ProtocolVersion = continuation.OriginalRequest.ProtocolVersion,
            Succeeded = false,
            StatusCode = StatusCodes.InternalError,
            Stage = WorkerStages.Recovery,
            HeartbeatUtc = _clock.UtcNow,
            Diagnostics = new System.Collections.Generic.List<DiagnosticRecord>
            {
                DiagnosticRecord.Create("WORKFLOW_STEP_EXCEPTION", DiagnosticSeverity.Error, ex.Message)
            }
        });
    }

    /// <summary>Attempts to fault an untyped continuation.</summary>
    private void TryFaultContinuation(object rawContinuation, Exception ex)
    {
        if (rawContinuation is WorkflowContinuation<MessageWorkflowContext> msgCont)
        {
            FaultContinuation(msgCont, ex);
        }
        else if (rawContinuation is WorkflowCompletionMarker<MessageWorkflowContext> marker)
        {
            FaultContinuation(marker.Inner, ex);
        }
    }

    // ── Queue state & active tracking ───────────────────────────────

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
