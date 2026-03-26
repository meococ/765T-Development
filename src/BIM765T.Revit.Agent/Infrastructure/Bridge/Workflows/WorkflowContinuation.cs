using System.Threading.Tasks;
using BIM765T.Revit.Contracts.Bridge;

namespace BIM765T.Revit.Agent.Infrastructure.Bridge.Workflows;

/// <summary>
/// Pairs a workflow's mutable context with the next step to execute
/// and the <see cref="TaskCompletionSource{T}"/> that the named-pipe caller is awaiting.
/// Immutable in shape — use <see cref="WithStep"/> to advance.
/// </summary>
internal sealed class WorkflowContinuation<TContext>
{
    internal WorkflowContinuation(
        TContext context,
        object nextStep,
        TaskCompletionSource<ToolResponseEnvelope> completion,
        ToolRequestEnvelope originalRequest)
    {
        Context = context;
        NextStep = nextStep;
        Completion = completion;
        OriginalRequest = originalRequest;
    }

    /// <summary>Mutable context bag shared across all steps.</summary>
    internal TContext Context { get; }

    /// <summary>
    /// The next step to execute — either <see cref="IWorkflowStep{TContext}"/>
    /// (runs on UI thread) or <see cref="IAsyncYieldStep{TContext}"/> (runs off thread).
    /// </summary>
    internal object NextStep { get; }

    /// <summary>
    /// The TCS that the pipe listener is blocking on.
    /// Resolved when the workflow completes or faults.
    /// </summary>
    internal TaskCompletionSource<ToolResponseEnvelope> Completion { get; }

    /// <summary>Original pipe request — used for building error responses.</summary>
    internal ToolRequestEnvelope OriginalRequest { get; }

    /// <summary>Returns a new continuation advanced to <paramref name="step"/>.</summary>
    internal WorkflowContinuation<TContext> WithStep(object step)
        => new WorkflowContinuation<TContext>(Context, step, Completion, OriginalRequest);
}
