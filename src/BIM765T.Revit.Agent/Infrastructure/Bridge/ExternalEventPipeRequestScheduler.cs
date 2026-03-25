using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Autodesk.Revit.UI;
using BIM765T.Revit.Agent.Infrastructure.Logging;
using BIM765T.Revit.Agent.Services.Bridge;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Agent.Infrastructure.Bridge;

internal sealed class ExternalEventPipeRequestScheduler : IPipeRequestScheduler
{
    private readonly ToolInvocationQueue _queue;
    private readonly ExternalEvent _externalEvent;
    private readonly IAgentLogger _logger;

    internal ExternalEventPipeRequestScheduler(ToolInvocationQueue queue, ExternalEvent externalEvent, IAgentLogger logger)
    {
        _queue = queue;
        _externalEvent = externalEvent;
        _logger = logger;
    }

    public async Task<ToolResponseEnvelope> ScheduleAsync(ToolRequestEnvelope request, int timeoutMs, CancellationToken token)
    {
        request ??= new ToolRequestEnvelope();
        request.RequestedPriority = ToolQueuePriorityResolver.NormalizePriority(request.RequestedPriority);
        var pending = new PendingToolInvocation(request);
        _queue.Enqueue(pending);
        var raiseResult = _externalEvent.Raise();

        if (raiseResult == ExternalEventRequest.Pending)
        {
            _logger.Info("ExternalEvent already pending; request queued and waiting.");
        }
        else if (raiseResult != ExternalEventRequest.Accepted)
        {
            pending.TryCancelBeforeExecution();
            _logger.Error($"ExternalEvent.Raise() returned {raiseResult} for {request.ToolName}. Invocation cancelled.");
            return ToolResponses.Failure(
                request,
                StatusCodes.RevitUnavailable,
                DiagnosticRecord.Create(
                    "EXTERNAL_EVENT_NOT_ACCEPTED",
                    DiagnosticSeverity.Error,
                    "ExternalEvent raise result: " + raiseResult + ". Invocation cancelled safely."));
        }

        return await AwaitCompletionAsync(pending, TimeSpan.FromMilliseconds(timeoutMs), token).ConfigureAwait(false);
    }

    private static async Task<ToolResponseEnvelope> AwaitCompletionAsync(PendingToolInvocation pending, TimeSpan timeout, CancellationToken token)
    {
        var completionTask = pending.Completion.Task;
        var completed = await Task.WhenAny(completionTask, Task.Delay(timeout, token)).ConfigureAwait(false);
        if (completed == completionTask)
        {
            return await completionTask.ConfigureAwait(false);
        }

        if (!pending.TryCancelBeforeExecution())
        {
            return await completionTask.ConfigureAwait(false);
        }

        return new ToolResponseEnvelope
        {
            RequestId = pending.Request.RequestId,
            ToolName = pending.Request.ToolName,
            CorrelationId = pending.Request.CorrelationId,
            ProtocolVersion = pending.Request.ProtocolVersion,
            Succeeded = false,
            StatusCode = StatusCodes.Timeout,
            Diagnostics = new List<DiagnosticRecord>
            {
                DiagnosticRecord.Create(
                    "TIMEOUT",
                    DiagnosticSeverity.Error,
                    $"Timed out after {timeout.TotalMilliseconds:F0}ms. Invocation was cancelled before any Revit execution.")
            }
        };
    }
}
