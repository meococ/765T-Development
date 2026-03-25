using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Agent.Infrastructure.Bridge;

internal sealed class PendingToolInvocation
{
    private const int StatePending = 0;
    private const int StateExecuting = 1;
    private const int StateCancelled = 2;
    private const int StateCompleted = 3;
    private int _state;
    private readonly ToolInvocationProfile _profile;

    internal PendingToolInvocation(ToolRequestEnvelope request, ToolManifest? manifest = null)
    {
        Request = request ?? new ToolRequestEnvelope();
        _profile = ToolQueuePriorityResolver.Resolve(Request, manifest);
        Completion = new TaskCompletionSource<ToolResponseEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    internal ToolRequestEnvelope Request { get; }

    internal TaskCompletionSource<ToolResponseEnvelope> Completion { get; }

    internal string Priority => _profile.Priority;

    internal string RiskTier => _profile.RiskTier;

    internal string ExecutionTier => _profile.ExecutionTier;

    internal string LatencyClass => _profile.LatencyClass;

    internal bool IsCancelled => Volatile.Read(ref _state) == StateCancelled;

    internal bool TryBeginExecution()
    {
        return Interlocked.CompareExchange(ref _state, StateExecuting, StatePending) == StatePending;
    }

    internal bool TryCancelBeforeExecution()
    {
        if (Interlocked.CompareExchange(ref _state, StateCancelled, StatePending) != StatePending)
        {
            return false;
        }

        Completion.TrySetResult(new ToolResponseEnvelope
        {
            RequestId = Request.RequestId,
            ToolName = Request.ToolName,
            CorrelationId = Request.CorrelationId,
            ProtocolVersion = Request.ProtocolVersion,
            Succeeded = false,
            StatusCode = StatusCodes.Timeout,
            Stage = WorkerStages.Recovery,
            HeartbeatUtc = DateTime.UtcNow,
            ExecutionTier = ExecutionTier,
            Diagnostics = new List<DiagnosticRecord>
            {
                DiagnosticRecord.Create("CANCELLED", DiagnosticSeverity.Warning, "Invocation da bi huy truoc khi Revit execute.")
            }
        });

        return true;
    }

    internal void MarkCompleted()
    {
        while (true)
        {
            var current = Volatile.Read(ref _state);
            if (current == StateCancelled || current == StateCompleted)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref _state, StateCompleted, current) == current)
            {
                return;
            }
        }
    }
}
