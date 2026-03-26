using Autodesk.Revit.UI;
using BIM765T.Revit.Agent.Services.Platform;

namespace BIM765T.Revit.Agent.Infrastructure.Bridge.Workflows.Steps;

/// <summary>
/// Step 3 (sync, UI thread): Dispatches to the appropriate intent handler.
/// Reads <see cref="MessageWorkflowContext.Decision"/> (populated by <see cref="PlanStep"/>).
/// Calls Revit API for QC, sheet analysis, family analysis, etc.
/// Populates ResponseText, ToolCards, ActionCards, ArtifactRefs.
/// Yields to <see cref="EnhanceStep"/> on the thread pool.
/// </summary>
internal sealed class ExecuteIntentStep : IWorkflowStep<MessageWorkflowContext>
{
    private readonly WorkerService _worker;

    internal ExecuteIntentStep(WorkerService worker)
    {
        _worker = worker;
    }

    public object? ExecuteOnUIThread(UIApplication uiapp, MessageWorkflowContext context)
    {
        _worker.ExecuteIntentForWorkflow(uiapp, context);

        // If LLM enhancer is configured, yield to thread pool for narration.
        // Otherwise, skip straight to BuildResponse on UI thread.
        if (_worker.Enhancer != null && _worker.Enhancer.IsLlmConfigured)
        {
            return new EnhanceStep(_worker);
        }

        // No LLM — go directly to final response assembly.
        return new BuildResponseStep(_worker);
    }
}
