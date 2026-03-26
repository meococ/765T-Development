using System;
using System.Threading.Tasks;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Agent.Infrastructure.Bridge.Workflows;

/// <summary>
/// Registration entry for a workflow-based tool.
/// The factory produces the initial context and first step;
/// the <see cref="ToolExternalEventHandler"/> drives the step machine.
/// </summary>
internal sealed class WorkflowRegistration
{
    internal ToolManifest Manifest { get; set; } = new ToolManifest();

    /// <summary>
    /// Factory: given a pipe request envelope, returns
    /// (WorkflowContinuation that wraps the context + first step + TCS).
    /// The handler infrastructure calls this once, then drives the continuation.
    /// </summary>
    internal Func<ToolRequestEnvelope, WorkflowSetup> Factory { get; set; }
        = _ => throw new InvalidOperationException("Workflow factory not configured.");
}

/// <summary>
/// The initial workflow setup returned by the factory.
/// Contains the first step and the mutable context.
/// </summary>
internal sealed class WorkflowSetup
{
    internal object FirstStep { get; set; } = null!;
    internal object Context { get; set; } = null!;

    /// <summary>
    /// Builds a typed <see cref="WorkflowContinuation{TContext}"/> from this setup.
    /// </summary>
    internal WorkflowContinuation<TContext> ToContinuation<TContext>(
        TaskCompletionSource<ToolResponseEnvelope> tcs,
        ToolRequestEnvelope request)
    {
        return new WorkflowContinuation<TContext>(
            (TContext)Context,
            FirstStep,
            tcs,
            request);
    }
}
