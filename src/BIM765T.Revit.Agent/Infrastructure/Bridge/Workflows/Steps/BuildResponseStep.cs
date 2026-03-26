using Autodesk.Revit.UI;
using BIM765T.Revit.Agent.Services.Platform;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Serialization;

namespace BIM765T.Revit.Agent.Infrastructure.Bridge.Workflows.Steps;

/// <summary>
/// Step 5 (sync, UI thread): Assembles the final <see cref="WorkerResponse"/>.
/// Reads all populated fields from context, builds remaining UI widgets,
/// persists episode, and signals workflow completion.
/// Returns null to indicate the workflow is complete.
/// </summary>
internal sealed class BuildResponseStep : IWorkflowStep<MessageWorkflowContext>
{
    private readonly WorkerService _worker;

    internal BuildResponseStep(WorkerService worker)
    {
        _worker = worker;
    }

    public object? ExecuteOnUIThread(UIApplication uiapp, MessageWorkflowContext context)
    {
        var response = _worker.BuildResponseForWorkflow(uiapp, context);
        context.FinalResponse = response;

        // null = workflow complete. The handler will read ctx.FinalResponse
        // and resolve the TaskCompletionSource.
        return null;
    }
}
