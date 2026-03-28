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
            "greeting" => "Worker da san sang cho nhiem vu tiep theo.",
            "identity_query" => "Worker da gioi thieu vai tro va lane ho tro hien tai.",
            "help" => "Worker da goi y cac diem vao an toan.",
            "context_query" => "Da tong hop context hien tai.",
            "project_research_request" => "Da tong hop thong tin project.",
            "qc_request" => "Da kiem tra model health.",
            "family_analysis_request" => "Da phan tich family.",
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
        var docTitle = contextSummary?.DocumentTitle ?? "khong ro";
        var viewName = contextSummary?.ActiveViewName ?? "khong ro";

        return intent switch
        {
            "greeting" =>
                $"Chao anh. Em dang o file {docTitle}, view {viewName}. " +
                "Anh can em ho tro gi — kiem tra model, phan tich sheet, hay xem context hien tai?",
            "identity_query" =>
                "Em la 765T Worker, tro ly BIM chay truc tiep trong Revit. " +
                $"Hien tai em dang doc file {docTitle}. " +
                "Em co the kiem tra context, ra QC read-only, phan tich family/sheet, hoac ho tro mutation khi anh can.",
            "help" =>
                $"Hien em dang o file {docTitle}, view {viewName}. " +
                "Anh co the thu: 'kiem tra model health', 'context hien tai', hoac 'phan tich family' de bat dau.",
            "context_query" =>
                $"Document: {docTitle}. Active view: {viewName}. " +
                $"Selection: {contextSummary?.SelectionCount ?? 0} phan tu.",
            "project_research_request" =>
                $"Em dang tong hop thong tin project {docTitle}. " +
                "Bao gom context hien tai, workspace bundle, va evidence tu memory.",
            "qc_request" =>
                $"Em se kiem tra model health cho file {docTitle}. " +
                "Day la QC read-only, khong thay doi gi trong model.",
            "family_analysis_request" =>
                $"Em se phan tich family hien tai trong {docTitle}.",
            _ => "Em chua xac dinh ro y dinh. Anh thu noi cu the hon."
        };
    }

    private static string BuildConversationalGoal(string intent)
    {
        return intent switch
        {
            "greeting" => "Chao nguoi dung va goi y buoc tiep theo.",
            "identity_query" => "Gioi thieu ban than va kha nang hien tai.",
            "help" => "Goi y cac diem vao an toan.",
            "context_query" => "Tong hop context Revit hien tai.",
            "project_research_request" => "Tong hop hien trang du an.",
            "qc_request" => "Kiem tra model health read-only.",
            "family_analysis_request" => "Phan tich family hien tai.",
            _ => "Tra loi conversational."
        };
    }
}
