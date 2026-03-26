using System.Threading;
using System.Threading.Tasks;
using BIM765T.Revit.Agent.Services.Platform;

namespace BIM765T.Revit.Agent.Infrastructure.Bridge.Workflows.Steps;

/// <summary>
/// Step 2 (async, thread pool): Runs LLM planning off the UI thread.
/// Rule-based classification is instant (microseconds), only the LLM HTTP call
/// goes async via <see cref="WorkerReasoningEngine.ProcessMessageAsync"/>.
/// Populates <see cref="MessageWorkflowContext.Decision"/>.
/// Yields back to <see cref="ExecuteIntentStep"/> on the UI thread.
/// </summary>
internal sealed class PlanStep : IAsyncYieldStep<MessageWorkflowContext>
{
    private readonly WorkerService _worker;

    internal PlanStep(WorkerService worker)
    {
        _worker = worker;
    }

    public async Task<IWorkflowStep<MessageWorkflowContext>> ExecuteOffThreadAsync(
        MessageWorkflowContext context, CancellationToken cancellationToken)
    {
        // ProcessMessageAsync: rule classification (sync, instant) + LLM planner (async HTTP).
        // No Revit API calls — safe to run on thread pool.
        var decision = await _worker.Reasoning.ProcessMessageAsync(
            context.Session,
            context.Request.Message,
            context.Request.ContinueMission,
            context.ContextSummary,
            context.WorkspaceId,
            cancellationToken).ConfigureAwait(false);

        context.Decision = decision;

        // Return to UI thread for intent execution (Revit API needed).
        return new ExecuteIntentStep(_worker);
    }
}
