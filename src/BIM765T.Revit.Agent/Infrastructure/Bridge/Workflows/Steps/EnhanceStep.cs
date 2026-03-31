using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BIM765T.Revit.Agent.Services.Platform;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Agent.Infrastructure.Bridge.Workflows.Steps;

/// <summary>
/// Step 4 (async, thread pool): Enhances response text via LLM.
/// No Revit API — safe to run off the UI thread.
/// Populates <see cref="MessageWorkflowContext.NarrationMode"/> and
/// <see cref="MessageWorkflowContext.NarrationDiagnostics"/>.
/// Yields back to <see cref="BuildResponseStep"/> on the UI thread.
/// </summary>
internal sealed class EnhanceStep : IAsyncYieldStep<MessageWorkflowContext>
{
    private readonly WorkerService _worker;

    internal EnhanceStep(WorkerService worker)
    {
        _worker = worker;
    }

    public async Task<IWorkflowStep<MessageWorkflowContext>> ExecuteOffThreadAsync(
        MessageWorkflowContext context, CancellationToken cancellationToken)
    {
        var enhancer = _worker.Enhancer;
        if (enhancer == null || !enhancer.IsLlmConfigured)
        {
            context.NarrationMode = WorkerNarrationModes.RuleOnly;
            context.NarrationDiagnostics = "LLM narration client is not configured in the runtime add-in.";
            return new BuildResponseStep(_worker);
        }

        var persona = _worker.Personas.Resolve(context.Session.PersonaId);
        var toolSummaries = context.ToolCards.Select(c => $"{c.ToolName}: {c.Summary}");

        var narration = await enhancer.EnhanceResponseAsync(
            context.Request.Message,
            context.Decision.Intent,
            context.ResponseText,
            toolSummaries,
            context.ContextSummary,
            persona,
            context.Session.Messages,
            context.Session.Mission.ReasoningSummary,
            context.Session.Mission.PlanSummary,
            cancellationToken).ConfigureAwait(false);

        context.ResponseText = narration.Text;
        context.NarrationMode = narration.Mode;
        context.NarrationDiagnostics = narration.Diagnostics;

        // Return to UI thread for final response assembly.
        return new BuildResponseStep(_worker);
    }
}
