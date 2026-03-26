using System;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Platform;

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BIM765T.Revit.Copilot.Core.Brain;

public sealed class WorkerReasoningEngine
{
    private readonly IntentClassifier _classifier;
    private readonly PersonaRegistry _personas;
    private readonly LlmResponseEnhancer? _enhancer;
    private readonly ILlmPlanner _planner;

    public WorkerReasoningEngine(IntentClassifier classifier, PersonaRegistry personas)
        : this(classifier, personas, null, null)
    {
    }

    public WorkerReasoningEngine(IntentClassifier classifier, PersonaRegistry personas, LlmResponseEnhancer? enhancer)
        : this(classifier, personas, enhancer, null)
    {
    }

    public WorkerReasoningEngine(IntentClassifier classifier, PersonaRegistry personas, LlmResponseEnhancer? enhancer, ILlmPlanner? planner)
    {
        _classifier = classifier ?? throw new ArgumentNullException(nameof(classifier));
        _personas = personas ?? throw new ArgumentNullException(nameof(personas));
        _enhancer = enhancer;
        _planner = planner ?? new NullLlmPlanner();
    }

    /// <summary>
    /// Exposes rule-based intent classification without full planning.
    /// Used by ConversationalStep fast-path to classify intent then route directly.
    /// </summary>
    public WorkerIntentClassification ClassifyIntent(string message, bool hasPendingApproval)
    {
        return _classifier.Classify(message, hasPendingApproval);
    }

    /// <summary>
    /// Returns true if the given intent is conversational (greeting, identity, help, context_query).
    /// These intents do NOT need LLM planning, tool chains, or capability compilation.
    /// </summary>
    public static bool IsConversationalIntent(string intent)
    {
        return string.Equals(intent, "greeting", StringComparison.OrdinalIgnoreCase)
            || string.Equals(intent, "identity_query", StringComparison.OrdinalIgnoreCase)
            || string.Equals(intent, "help", StringComparison.OrdinalIgnoreCase)
            || string.Equals(intent, "context_query", StringComparison.OrdinalIgnoreCase);
    }

    public WorkerDecision ProcessMessage(WorkerConversationSessionState session, string message, bool continueMission)
    {
        return ProcessMessage(session, message, continueMission, new WorkerContextSummary(), string.Empty);
    }

    public WorkerDecision ProcessMessage(WorkerConversationSessionState session, string message, bool continueMission, WorkerContextSummary? contextSummary, string workspaceId)
    {
        var persona = _personas.Resolve(session?.PersonaId);
        var classification = _classifier.Classify(message, session?.PendingApprovalState?.HasPendingApproval ?? false);
        var decision = new WorkerDecision
        {
            Intent = classification.Intent,
            Goal = BuildGoal(classification.Intent, classification.TargetHint),
            DecisionRationale = "Rule-first routing dua tren intent ro rang, history gan day, va lane Revit hien tai.",
            ReasoningSummary = $"Intent={classification.Intent}; persona={persona.PersonaId}; continueMission={continueMission}.",
            ResponseLead = BuildResponseLead(classification.Intent)
        };

        PopulatePlanByIntent(classification, decision);

        ApplyPlannerProposal(session ?? new WorkerConversationSessionState(), message, continueMission, contextSummary, workspaceId, persona, classification, decision);

        return decision;
    }

    /// <summary>
    /// Async version — runs LLM planning off the calling thread.
    /// Rule-based classification is still synchronous (instant), only the LLM planner call is async.
    /// </summary>
    public async Task<WorkerDecision> ProcessMessageAsync(
        WorkerConversationSessionState session, string message, bool continueMission,
        WorkerContextSummary? contextSummary, string workspaceId, CancellationToken cancellationToken)
    {
        var persona = _personas.Resolve(session?.PersonaId);
        var classification = _classifier.Classify(message, session?.PendingApprovalState?.HasPendingApproval ?? false);
        var decision = new WorkerDecision
        {
            Intent = classification.Intent,
            Goal = BuildGoal(classification.Intent, classification.TargetHint),
            DecisionRationale = "Rule-first routing dua tren intent ro rang, history gan day, va lane Revit hien tai.",
            ReasoningSummary = $"Intent={classification.Intent}; persona={persona.PersonaId}; continueMission={continueMission}.",
            ResponseLead = BuildResponseLead(classification.Intent)
        };

        PopulatePlanByIntent(classification, decision);

        await ApplyPlannerProposalAsync(
            session ?? new WorkerConversationSessionState(), message, continueMission,
            contextSummary, workspaceId, persona, classification, decision, cancellationToken).ConfigureAwait(false);

        return decision;
    }

    private void ApplyPlannerProposal(
        WorkerConversationSessionState session,
        string message,
        bool continueMission,
        WorkerContextSummary? contextSummary,
        string workspaceId,
        WorkerPersonaSummary persona,
        WorkerIntentClassification classification,
        WorkerDecision decision)
    {
        var profile = _planner.RuntimeProfile ?? new LlmProviderConfiguration();
        decision.ConfiguredProvider = profile.ConfiguredProvider;
        decision.PlannerModel = profile.PlannerPrimaryModel;
        decision.ResponseModel = profile.ResponseModel;
        decision.ReasoningMode = WorkerReasoningModes.RuleFirst;

        if (!_planner.IsConfigured)
        {
            return;
        }

        var validation = _planner.Plan(new LlmPlanningRequest
        {
            Session = session ?? new WorkerConversationSessionState(),
            RuleDecision = CloneDecision(decision),
            Classification = classification ?? new WorkerIntentClassification(),
            Persona = persona ?? new WorkerPersonaSummary(),
            ContextSummary = contextSummary ?? new WorkerContextSummary(),
            WorkspaceId = workspaceId ?? string.Empty,
            UserMessage = message ?? string.Empty,
            ContinueMission = continueMission
        });

        MergePlannerValidation(decision, validation);
    }

    /// <summary>
    /// Async planner proposal — runs LLM HTTP calls on background thread.
    /// Applies the same validation/merge logic as the sync version.
    /// </summary>
    private async Task ApplyPlannerProposalAsync(
        WorkerConversationSessionState session,
        string message,
        bool continueMission,
        WorkerContextSummary? contextSummary,
        string workspaceId,
        WorkerPersonaSummary persona,
        WorkerIntentClassification classification,
        WorkerDecision decision,
        CancellationToken cancellationToken)
    {
        var profile = _planner.RuntimeProfile ?? new LlmProviderConfiguration();
        decision.ConfiguredProvider = profile.ConfiguredProvider;
        decision.PlannerModel = profile.PlannerPrimaryModel;
        decision.ResponseModel = profile.ResponseModel;
        decision.ReasoningMode = WorkerReasoningModes.RuleFirst;

        if (!_planner.IsConfigured)
        {
            return;
        }

        var validation = await _planner.PlanAsync(new LlmPlanningRequest
        {
            Session = session ?? new WorkerConversationSessionState(),
            RuleDecision = CloneDecision(decision),
            Classification = classification ?? new WorkerIntentClassification(),
            Persona = persona ?? new WorkerPersonaSummary(),
            ContextSummary = contextSummary ?? new WorkerContextSummary(),
            WorkspaceId = workspaceId ?? string.Empty,
            UserMessage = message ?? string.Empty,
            ContinueMission = continueMission
        }, cancellationToken).ConfigureAwait(false);

        MergePlannerValidation(decision, validation);
    }

    /// <summary>Shared merge logic used by both sync and async planner paths.</summary>
    private static void MergePlannerValidation(WorkerDecision decision, LlmPlanValidationResult validation)
    {
        decision.ConfiguredProvider = validation.ConfiguredProvider;
        decision.PlannerModel = validation.PlannerModel;
        decision.ResponseModel = validation.ResponseModel;
        decision.ReasoningMode = validation.ReasoningMode;

        if (!validation.Accepted)
        {
            return;
        }

        var proposal = validation.Proposal ?? new LlmPlanProposal();
        if (CanPromoteIntent(decision.Intent, proposal.Intent))
        {
            decision.Intent = proposal.Intent;
        }

        if (!string.IsNullOrWhiteSpace(proposal.Goal))
        {
            decision.Goal = proposal.Goal;
        }

        if (!string.IsNullOrWhiteSpace(proposal.ReasoningSummary))
        {
            decision.ReasoningSummary = proposal.ReasoningSummary;
        }

        if (!string.IsNullOrWhiteSpace(proposal.PlanSummary))
        {
            decision.PlanSummary = proposal.PlanSummary;
        }

        if (validation.PlannedTools.Count > 0)
        {
            decision.PlannedTools = validation.PlannedTools.ToList();
        }

        decision.PreferredCommandId = validation.PreferredCommandId;
        decision.RequiresClarification = decision.RequiresClarification || proposal.RequiresClarification;

        if (proposal.RequiresClarification && !string.IsNullOrWhiteSpace(proposal.ClarificationQuestion))
        {
            if (!decision.SuggestedActions.Exists(x =>
                    string.Equals(x.ActionKind, WorkerActionKinds.Clarify, StringComparison.OrdinalIgnoreCase)))
            {
                decision.SuggestedActions.Insert(0, CreateSuggest(
                    "Lam ro them",
                    proposal.ClarificationQuestion,
                    string.Empty));
                decision.SuggestedActions[0].ActionKind = WorkerActionKinds.Clarify;
                decision.SuggestedActions[0].Title = "Lam ro them";
                decision.SuggestedActions[0].ToolName = string.Empty;
                decision.SuggestedActions[0].AutoExecutionEligible = false;
            }
        }
    }

    /// <summary>
    /// Populates the decision's planned tools and suggested actions based on rule-classified intent.
    /// Extracted from ProcessMessage for reuse in async path.
    /// </summary>
    private static void PopulatePlanByIntent(WorkerIntentClassification classification, WorkerDecision decision)
    {
        switch (classification.Intent)
        {
            case "greeting":
                decision.PlanSummary = "Chao anh, em se lay context roi de xuat buoc read-only truoc.";
                decision.SuggestedActions.Add(CreateSuggest("Kiem tra model health", "Chay QC read-only cho model hien tai.", ToolNames.ReviewSmartQc));
                decision.SuggestedActions.Add(CreateSuggest("Xem context hien tai", "Lay document, view, selection va delta summary.", ToolNames.ContextGetDeltaSummary));
                break;

            case "identity_query":
                decision.PlanSummary = "Xac nhan vai tro worker, context dang mo, va cac lane em co the ho tro ngay trong Revit.";
                decision.SuggestedActions.Add(CreateSuggest("Xem context hien tai", "Lay document, view, selection va delta summary.", ToolNames.ContextGetDeltaSummary));
                decision.SuggestedActions.Add(CreateSuggest("Kiem tra model health", "Chay QC read-only cho model hien tai.", ToolNames.ReviewSmartQc));
                break;

            case "context_query":
                decision.PlanSummary = "Doc context hien tai, delta gan day, va goi y buoc ke tiep.";
                decision.PlannedTools.Add(ToolNames.SessionGetTaskContext);
                decision.PlannedTools.Add(ToolNames.ContextGetDeltaSummary);
                break;

            case "project_research_request":
                decision.PlanSummary = "Chay grounded research loop 3 buoc: live context -> workspace context bundle -> deep-scan/artifact/standards/memory evidence neu co.";
                decision.PlannedTools.Add(ToolNames.SessionGetTaskContext);
                decision.PlannedTools.Add(ToolNames.ContextGetDeltaSummary);
                decision.PlannedTools.Add(ToolNames.ProjectGetContextBundle);
                decision.PlannedTools.Add(ToolNames.ProjectGetDeepScan);
                decision.PlannedTools.Add(ToolNames.StandardsResolve);
                decision.PlannedTools.Add(ToolNames.ArtifactSummarize);
                decision.PlannedTools.Add(ToolNames.MemorySearchScoped);
                decision.SuggestedActions.Add(CreateSuggest("Project overview grounded", "Tom tat current state dua tren live context + workspace bundle + deep scan neu san co.", ToolNames.ProjectGetContextBundle));
                break;

            case "sheet_analysis_request":
                decision.PlanSummary = "Phan tich sheet bang summary + QC read-only, giu lane MVP gon va co evidence ro rang.";
                decision.PlannedTools.Add(ToolNames.ReviewSheetSummary);
                decision.PlannedTools.Add(ToolNames.ReviewSmartQc);
                break;

            case "sheet_authoring_request":
                decision.PlanSummary = "Resolve workspace standards + playbook truoc, sau do preview tool chain tao sheet/view/place/QC.";
                decision.PlannedTools.Add(ToolNames.WorkspaceGetManifest);
                decision.PlannedTools.Add(ToolNames.StandardsResolve);
                decision.PlannedTools.Add(ToolNames.PlaybookMatch);
                decision.PlannedTools.Add(ToolNames.PlaybookPreview);
                decision.SuggestedActions.Add(CreateSuggest("Preview playbook sheet", "Resolve standards team va chain tao sheet truoc khi mutate.", ToolNames.PlaybookPreview));
                break;

            case "view_authoring_request":
                decision.PlanSummary = "Dung atlas fast-path truoc: quick-plan -> command.execute_safe; fallback ve playbook neu can.";
                decision.PlannedTools.Add(ToolNames.WorkflowQuickPlan);
                decision.PlannedTools.Add(ToolNames.CommandDescribe);
                decision.PlannedTools.Add(ToolNames.CommandExecuteSafe);
                decision.SuggestedActions.Add(CreateSuggest("Quick view action", "Resolve create/duplicate/apply-template tu command atlas.", ToolNames.WorkflowQuickPlan));
                break;

            case "documentation_request":
                decision.PlanSummary = "Resolve documentation quick-path truoc, sau do fallback sang playbook sheet package neu task nhieu buoc.";
                decision.PlannedTools.Add(ToolNames.WorkflowQuickPlan);
                decision.PlannedTools.Add(ToolNames.CommandExecuteSafe);
                decision.PlannedTools.Add(ToolNames.PlaybookPreview);
                break;

            case "model_manage_request":
                decision.PlanSummary = "Route vao model-manage quick-path de preview purge/cleanup/rename, giu approval gate neu co mutation.";
                decision.PlannedTools.Add(ToolNames.WorkflowQuickPlan);
                decision.PlannedTools.Add(ToolNames.CommandExecuteSafe);
                decision.PlannedTools.Add(ToolNames.CommandCoverageReport);
                break;

            case "command_palette_request":
                decision.PlanSummary = "Treat request as command-palette lookup: search -> describe -> quick-plan -> execute if safe.";
                decision.PlannedTools.Add(ToolNames.CommandSearch);
                decision.PlannedTools.Add(ToolNames.CommandDescribe);
                decision.PlannedTools.Add(ToolNames.WorkflowQuickPlan);
                break;

            case "element_authoring_request":
                decision.PlanSummary = "Try atlas quick-path first for atomic authoring; if no safe lane exists, escalate to clarify/playbook.";
                decision.PlannedTools.Add(ToolNames.CommandSearch);
                decision.PlannedTools.Add(ToolNames.WorkflowQuickPlan);
                decision.PlannedTools.Add(ToolNames.CommandExecuteSafe);
                break;

            case "governance_request":
                decision.PlanSummary = "Resolve governance domain, policy packs, playbook, va specialist truoc khi chon leaf tools.";
                decision.PlannedTools.Add(ToolNames.ToolGetGuidance);
                decision.PlannedTools.Add(ToolNames.PlaybookMatch);
                decision.PlannedTools.Add(ToolNames.PolicyResolve);
                decision.PlannedTools.Add(ToolNames.SpecialistResolve);
                decision.PlannedTools.Add(ToolNames.IntentCompile);
                decision.SuggestedActions.Add(CreateSuggest("Governance compile", "Compile task governance theo policy + playbook workspace.", ToolNames.IntentCompile));
                break;

            case "annotation_request":
                decision.PlanSummary = "Dung annotation/governance playbook va policy packs de plan tag/dim/room-finish safely.";
                decision.PlannedTools.Add(ToolNames.ToolGetGuidance);
                decision.PlannedTools.Add(ToolNames.PlaybookMatch);
                decision.PlannedTools.Add(ToolNames.PolicyResolve);
                decision.PlannedTools.Add(ToolNames.IntentCompile);
                break;

            case "coordination_request":
                decision.PlanSummary = "Route vao coordination lane: guidance -> playbook -> policy -> specialist -> compiled fix/verify plan.";
                decision.PlannedTools.Add(ToolNames.ToolGetGuidance);
                decision.PlannedTools.Add(ToolNames.PlaybookMatch);
                decision.PlannedTools.Add(ToolNames.PolicyResolve);
                decision.PlannedTools.Add(ToolNames.SpecialistResolve);
                decision.PlannedTools.Add(ToolNames.IntentCompile);
                decision.SuggestedActions.Add(CreateSuggest("Preview clash plan", "Compile clash/opening task truoc khi vao fix loop.", ToolNames.IntentCompile));
                break;

            case "systems_request":
                decision.PlanSummary = "Route vao systems lane: capture graph, compile task, va plan disconnected/slope/routing fixes.";
                decision.PlannedTools.Add(ToolNames.ToolGetGuidance);
                decision.PlannedTools.Add(ToolNames.SystemCaptureGraph);
                decision.PlannedTools.Add(ToolNames.PolicyResolve);
                decision.PlannedTools.Add(ToolNames.SpecialistResolve);
                decision.PlannedTools.Add(ToolNames.IntentCompile);
                decision.SuggestedActions.Add(CreateSuggest("Capture system graph", "Lay snapshot connectivity/slope truoc khi plan fix.", ToolNames.SystemCaptureGraph));
                break;

            case "integration_request":
                decision.PlanSummary = "Route vao integration lane: compile task, preview sync delta, va giu boundary cloud/plugin-safe.";
                decision.PlannedTools.Add(ToolNames.ToolGetGuidance);
                decision.PlannedTools.Add(ToolNames.IntentCompile);
                decision.PlannedTools.Add(ToolNames.IntegrationPreviewSync);
                decision.PlannedTools.Add(ToolNames.PolicyResolve);
                break;

            case "intent_compile_request":
                decision.PlanSummary = "Compile natural-language request thanh typed task plan, validate tool lanes, roi moi de xuat execute.";
                decision.PlannedTools.Add(ToolNames.IntentCompile);
                decision.PlannedTools.Add(ToolNames.IntentValidate);
                decision.PlannedTools.Add(ToolNames.ToolGetGuidance);
                break;

            case "family_analysis_request":
                decision.PlanSummary = "Phan tich family hien tai hoac family duoc chi dinh bang X-Ray.";
                decision.PlannedTools.Add(ToolNames.FamilyXray);
                break;

            case "qc_request":
                decision.PlanSummary = "Chay model QC read-only, roi tom tat findings va next actions.";
                decision.PlannedTools.Add(ToolNames.ReviewModelHealth);
                decision.PlannedTools.Add(ToolNames.ReviewSmartQc);
                break;

            case "mutation_request":
                decision.PlanSummary = "Khong mutate thang. Em se co preview hoac chan de xin ro pham vi truoc.";
                decision.SuggestedActions.Add(new WorkerActionCard
                {
                    ActionKind = WorkerActionKinds.Clarify,
                    Title = "Noi ro mutation",
                    Summary = "Neu ro element/scope/tool mong muon, vi du purge unused hay fill parameter.",
                    IsPrimary = true,
                    ExecutionTier = WorkerExecutionTiers.Tier1,
                    WhyThisAction = "Mutation lane can scope ro truoc khi worker de xuat tool an toan.",
                    Confidence = 0.8d,
                    RecoveryHint = "Neu can mutate ngay, hay noi ro family, element, parameter, view, hoac workflow.",
                    AutoExecutionEligible = false
                });
                break;

            case "approval":
                decision.PlanSummary = "Co approval pending, em se validate token roi execute theo kernel an toan.";
                decision.PlannedTools.Add("pending_approval");
                break;

            case "reject":
                decision.PlanSummary = "Huy pending approval va giu mission o trang thai blocked.";
                break;

            case "resume":
                decision.PlanSummary = "Tiep tuc mission gan nhat neu trang thai va context con hop le.";
                decision.PlannedTools.Add("resume_mission");
                break;

            case "cancel":
                decision.PlanSummary = "Ket thuc mission hien tai, khong thuc thi gi them.";
                break;

            default:
                decision.RequiresClarification = true;
                decision.PlanSummary = "Em chua map duoc intent du chac. Em se hoi lai ngan va de xuat tool an toan truoc.";
                decision.SuggestedActions.Add(CreateSuggest("Kiem tra model health", "Chay QC read-only cho model hien tai.", ToolNames.ReviewSmartQc));
                decision.SuggestedActions.Add(CreateSuggest("Lay context", "Doc active doc/view/selection va delta.", ToolNames.ContextGetDeltaSummary));
                break;
        }
    }

    private static bool CanPromoteIntent(string currentIntent, string proposedIntent)
    {
        if (string.IsNullOrWhiteSpace(proposedIntent) || string.Equals(currentIntent, proposedIntent, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.Equals(currentIntent, "mutation_request", StringComparison.OrdinalIgnoreCase)
            || string.Equals(currentIntent, "help", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static WorkerDecision CloneDecision(WorkerDecision source)
    {
        return new WorkerDecision
        {
            Intent = source.Intent,
            Goal = source.Goal,
            ReasoningSummary = source.ReasoningSummary,
            PlanSummary = source.PlanSummary,
            DecisionRationale = source.DecisionRationale,
            ResponseLead = source.ResponseLead,
            RequiresClarification = source.RequiresClarification,
            PlannedTools = source.PlannedTools?.ToList() ?? new List<string>(),
            SuggestedActions = source.SuggestedActions?.ToList() ?? new List<WorkerActionCard>(),
            PreferredCommandId = source.PreferredCommandId,
            ConfiguredProvider = source.ConfiguredProvider,
            PlannerModel = source.PlannerModel,
            ResponseModel = source.ResponseModel,
            ReasoningMode = source.ReasoningMode
        };
    }

    private static WorkerActionCard CreateSuggest(string title, string summary, string toolName)
    {
        return new WorkerActionCard
        {
            ActionKind = WorkerActionKinds.Suggest,
            Title = title,
            Summary = summary,
            ToolName = toolName,
            ExecutionTier = toolName.StartsWith("audit.", StringComparison.OrdinalIgnoreCase)
                || toolName.StartsWith("workflow.", StringComparison.OrdinalIgnoreCase)
                ? WorkerExecutionTiers.Tier1
                : WorkerExecutionTiers.Tier0,
            WhyThisAction = "Suggestion nay duoc route theo intent + persona + lane Revit hien tai.",
            Confidence = 0.86d,
            RecoveryHint = "Neu suggestion chua dung scope, hay mo ta lai document/view/selection muc tieu.",
            AutoExecutionEligible = false
        };
    }

    private static string BuildResponseLead(string intent)
    {
        if (string.Equals(intent, "greeting", StringComparison.OrdinalIgnoreCase))
        {
            return "Em dang dong bo context Revit va san sang nhan task tiep theo.";
        }

        if (string.Equals(intent, "identity_query", StringComparison.OrdinalIgnoreCase))
        {
            return "Em dang xac nhan vai tro worker va context dang mo de anh biet em co the lam gi ngay luc nay.";
        }

        return "Em dang lap plan an toan truoc khi chon tool hoac preview.";
    }

    private static string BuildGoal(string intent, string targetHint)
    {
        switch (intent)
        {
            case "context_query":
                return "Nam ngu canh Revit hien tai truoc khi hanh dong.";
            case "identity_query":
                return "Gioi thieu vai tro worker va kha nang trong model dang mo.";
            case "project_research_request":
                return "Tong hop hien trang du an theo live Revit context, workspace context bundle, va deep-scan evidence neu san co.";
            case "sheet_analysis_request":
                return string.IsNullOrWhiteSpace(targetHint) ? "Phan tich sheet hien tai." : $"Phan tich sheet {targetHint}.";
            case "sheet_authoring_request":
                return string.IsNullOrWhiteSpace(targetHint) ? "Tao sheet theo chuan workspace." : $"Tao/preview sheet {targetHint} theo chuan workspace.";
            case "view_authoring_request":
                return "Thuc hien quick action cho view an toan, context-aware, va fallback sang playbook neu can.";
            case "documentation_request":
                return "Thuc hien documentation quick-path hoac package workflow tuy muc do phuc tap.";
            case "model_manage_request":
                return "Preview va execute cac lenh manage/cleanup theo quick-path an toan.";
            case "command_palette_request":
                return "Resolve lenh tu command atlas, khong phai tool hunt bang context phinh to.";
            case "element_authoring_request":
                return "Thu authoring atomic qua atlas fast-path truoc khi vao workflow lon.";
            case "governance_request":
                return "Resolve governance standards, playbooks, va fix lanes theo workspace.";
            case "annotation_request":
                return "Plan annotation/task room-finish theo policy va playbook an toan.";
            case "coordination_request":
                return "Lap coordination plan cho clash/clearance/opening voi preview + verify.";
            case "systems_request":
                return "Lap systems plan cho connectivity/slope/routing voi graph snapshot truoc.";
            case "integration_request":
                return "Preview integration delta va giu external sync o lane connector-safe.";
            case "intent_compile_request":
                return "Compile natural-language request thanh typed plan co the validate.";
            case "family_analysis_request":
                return string.IsNullOrWhiteSpace(targetHint) ? "Phan tich family hien tai." : $"Phan tich family {targetHint}.";
            case "qc_request":
                return "QC model hien tai theo huong read-only.";
            case "mutation_request":
                return "Lap plan mutate an toan, preview truoc khi execute.";
            case "approval":
                return "Thuc thi preview da duoc approve.";
            case "reject":
                return "Huy approval pending mot cach an toan.";
            case "resume":
                return "Tiep tuc mission con dang do.";
            case "cancel":
                return "Dung mission hien tai.";
            default:
                return "Ho tro worker theo ngu canh Revit hien tai.";
        }
    }
}
