using Autodesk.Revit.UI;
using BIM765T.Revit.Agent.Services.Platform;
using BIM765T.Revit.Copilot.Core.Brain;

namespace BIM765T.Revit.Agent.Infrastructure.Bridge.Workflows.Steps;

/// <summary>
/// Step 1 (sync, UI thread): Resolves Revit document, session, context summary.
/// Populates <see cref="MessageWorkflowContext"/> with Doc, Session, WorkspaceId, ContextSummary.
///
/// After gathering context, performs instant intent classification to decide the route:
///   - Conversational/read-only intents (greeting, identity, help, context_query,
///     project_research_request, qc_request, family_analysis_request)
///     -> ConversationalStep (1 async LLM call -> BuildResponse). Total: 3 steps, 1-3s.
///   - Action intents (mutation, sheet/view authoring, ...)
///     -> PlanStep -> ExecuteIntent -> Enhance -> BuildResponse. Total: 5 steps, 5-18s.
///
/// ~100ms typical.
/// </summary>
internal sealed class GatherContextStep : IWorkflowStep<MessageWorkflowContext>
{
    private readonly WorkerService _worker;

    internal GatherContextStep(WorkerService worker)
    {
        _worker = worker;
    }

    public object? ExecuteOnUIThread(UIApplication uiapp, MessageWorkflowContext context)
    {
        var populated = _worker.GatherContextForWorkflow(uiapp, context.Envelope, context.Request);

        // Transfer gathered data into the shared context.
        context.Doc = populated.Doc;
        context.DocumentKey = populated.DocumentKey;
        context.Session = populated.Session;
        context.WorkspaceId = populated.WorkspaceId;
        context.ContextSummary = populated.ContextSummary;

        // Instant intent classification — decides fast-path vs full pipeline.
        var classification = _worker.Reasoning.ClassifyIntent(
            context.Request.Message,
            context.Session.PendingApprovalState?.HasPendingApproval ?? false);

        if (WorkerReasoningEngine.IsConversationalIntent(classification.Intent))
        {
            // Fast-path: 1 async LLM call → BuildResponse. Skips Plan + ExecuteIntent + Enhance.
            return new ConversationalStep(_worker);
        }

        // Full pipeline: Plan (thread pool LLM) → ExecuteIntent (UI) → Enhance (thread pool LLM) → BuildResponse (UI).
        return new PlanStep(_worker);
    }
}
