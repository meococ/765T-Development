namespace BIM765T.Revit.Agent.Infrastructure.Bridge.Workflows;

/// <summary>
/// Sentinel queued in the continuation queue when an async step finishes
/// and signals workflow completion. The handler drains this marker
/// and resolves the inner continuation's TCS on the UI thread.
/// </summary>
internal sealed class WorkflowCompletionMarker<TContext>
{
    internal WorkflowCompletionMarker(WorkflowContinuation<TContext> inner)
    {
        Inner = inner;
    }

    internal WorkflowContinuation<TContext> Inner { get; }
}
