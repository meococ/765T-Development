using System.Threading;
using System.Threading.Tasks;
using Autodesk.Revit.UI;

namespace BIM765T.Revit.Agent.Infrastructure.Bridge.Workflows;

/// <summary>
/// Sync step — runs on the Revit UI thread with full Revit API access.
/// Returns the next step (sync or async) to execute, or null to signal completion.
/// </summary>
internal interface IWorkflowStep<TContext>
{
    /// <summary>
    /// Executes on the UI thread.
    /// Returns a sync <see cref="IWorkflowStep{TContext}"/> or an
    /// async <see cref="IAsyncYieldStep{TContext}"/>, or null if workflow is complete.
    /// </summary>
    object? ExecuteOnUIThread(UIApplication uiapp, TContext context);
}

/// <summary>
/// Async step — runs on the thread pool, NO Revit API access.
/// <see cref="UIApplication"/> is intentionally omitted from the signature
/// to provide compile-time safety against accidental Revit API calls from background threads.
/// </summary>
internal interface IAsyncYieldStep<TContext>
{
    /// <summary>
    /// Runs on thread pool. Must NOT reference any Autodesk.Revit types.
    /// Returns the next <see cref="IWorkflowStep{TContext}"/> to resume on the UI thread.
    /// </summary>
    Task<IWorkflowStep<TContext>> ExecuteOffThreadAsync(TContext context, CancellationToken cancellationToken);
}
