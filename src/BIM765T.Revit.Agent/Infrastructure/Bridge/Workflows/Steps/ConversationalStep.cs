using System.Threading;
using System.Threading.Tasks;
using BIM765T.Revit.Agent.Services.Platform;
using BIM765T.Revit.Contracts.Platform;
using BIM765T.Revit.Copilot.Core.Brain;

namespace BIM765T.Revit.Agent.Infrastructure.Bridge.Workflows.Steps;

/// <summary>
/// Fast-path for conversational and read-only intents.
/// Covers: greeting, identity_query, help, context_query, project_research_request, qc_request, family_analysis_request.
/// Merges Plan + ExecuteIntent + Enhance into a SINGLE async step:
///   1. Rule-based classification (instant, already done in GatherContextStep)
///   2. ONE LLM call with compact prompt + Revit context (1-3s)
///   3. Skips: playbook match, capability compile, tool chain, separate enhance
///
/// Result: 3-step workflow (Gather -> Conversational -> BuildResponse) instead of 5.
/// User perceives ~1-3s response instead of 5-18s.
/// </summary>
internal sealed class ConversationalStep : IAsyncYieldStep<MessageWorkflowContext>
{
    private readonly WorkerService _worker;

    internal ConversationalStep(WorkerService worker)
    {
        _worker = worker;
    }

    public async Task<IWorkflowStep<MessageWorkflowContext>> ExecuteOffThreadAsync(
        MessageWorkflowContext context, CancellationToken cancellationToken)
    {
        var session = context.Session;
        var request = context.Request;
        var contextSummary = context.ContextSummary;

        // 1. Rule-based intent classification — instant, no LLM needed.
        var classification = _worker.Reasoning.ClassifyIntent(
            request.Message,
            session.PendingApprovalState?.HasPendingApproval ?? false);

        var decision = new WorkerDecision
        {
            Intent = classification.Intent,
            Goal = BuildConversationalGoal(classification.Intent),
            DecisionRationale = "Conversational fast-path — no planner needed.",
            ReasoningSummary = $"Intent={classification.Intent}; fast-path=true.",
            ResponseLead = string.Empty
        };

        context.Decision = decision;

        // 2. Mission bookkeeping — lightweight, no capability planning.
        _worker.Missions.EnsureMission(session, decision.Intent, decision.Goal, request.ContinueMission);
        _worker.Missions.SetPlan(session, decision);

        // 3. Single LLM call with compact prompt and tight timeout (8s).
        //    Replaces both Plan LLM + Enhance LLM from the full pipeline.
        var enhancer = _worker.Enhancer;
        if (enhancer != null && enhancer.IsLlmConfigured)
        {
            var persona = _worker.Personas.Resolve(session.PersonaId);
            var narration = await enhancer.EnhanceConversationalAsync(
                request.Message,
                classification.Intent,
                BuildFallbackText(classification.Intent, contextSummary),
                contextSummary,
                persona,
                session.Messages,
                cancellationToken).ConfigureAwait(false);

            context.ResponseText = narration.Text;
            context.NarrationMode = narration.Mode;
            context.NarrationDiagnostics = narration.Diagnostics;
        }
        else
        {
            // No LLM configured — use rule-based fallback text directly.
            context.ResponseText = BuildFallbackText(classification.Intent, contextSummary);
            context.NarrationMode = WorkerNarrationModes.RuleOnly;
            context.NarrationDiagnostics = "Conversational fast-path, no LLM configured.";
        }

        // 4. Complete mission immediately for conversational intents.
        var completionNote = classification.Intent switch
        {
            "greeting" => "Worker ready for the next task.",
            "identity_query" => "Worker introduced role and current support lanes.",
            "help" => "Worker suggested safe entry points.",
            "context_query" => "Current context summarized.",
            "project_research_request" => "Project information summarized.",
            "qc_request" => "Model health checked.",
            "family_analysis_request" => "Family analyzed.",
            _ => "Conversational response completed."
        };
        _worker.Missions.Complete(session, completionNote);

        // Skip ExecuteIntent + EnhanceStep entirely — go straight to BuildResponse.
        return new BuildResponseStep(_worker);
    }

    /// <summary>
    /// Fallback text when LLM is unavailable — same quality as existing rule-based responses.
    /// </summary>
    private static string BuildFallbackText(string intent, WorkerContextSummary contextSummary)
    {
        var docTitle = contextSummary?.DocumentTitle ?? "unknown";
        var viewName = contextSummary?.ActiveViewName ?? "unknown";

        return intent switch
        {
            "greeting" =>
                $"Hello. Currently in file {docTitle}, view {viewName}. " +
                "How can I help — check model health, analyze sheets, or view current context?",
            "identity_query" =>
                "I am 765T Worker, a BIM assistant running directly inside Revit. " +
                $"Currently reading file {docTitle}. " +
                "I can check context, run read-only QC, analyze families/sheets, or assist with mutations.",
            "help" =>
                $"Currently in file {docTitle}, view {viewName}. " +
                "Try: 'check model health', 'current context', or 'analyze family' to get started.",
            "context_query" =>
                $"Document: {docTitle}. Active view: {viewName}. " +
                $"Selection: {contextSummary?.SelectionCount ?? 0} element(s).",
            "project_research_request" =>
                $"Summarizing project information for {docTitle}. " +
                "Including current context, workspace bundle, and memory evidence.",
            "qc_request" =>
                $"Running model health check on {docTitle}. " +
                "This is read-only QC — no changes to the model.",
            "family_analysis_request" =>
                $"Analyzing families in {docTitle}.",
            _ => "Could not determine intent clearly. Please try being more specific."
        };
    }

    private static string BuildConversationalGoal(string intent)
    {
        return intent switch
        {
            "greeting" => "Greet user and suggest next steps.",
            "identity_query" => "Introduce self and current capabilities.",
            "help" => "Suggest safe entry points.",
            "context_query" => "Summarize current Revit context.",
            "project_research_request" => "Summarize project state.",
            "qc_request" => "Run read-only model health check.",
            "family_analysis_request" => "Analyze current family.",
            _ => "Conversational response."
        };
    }
}
