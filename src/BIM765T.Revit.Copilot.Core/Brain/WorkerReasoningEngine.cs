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
    /// Intents that can skip the full 5-step pipeline (Plan → ExecuteIntent → Enhance)
    /// and use the 3-step fast-path (Gather → Conversational → BuildResponse) instead.
    /// All of these are read-only or informational — no mutations, no approval gates.
    /// </summary>
    private static readonly HashSet<string> ConversationalIntents = new(StringComparer.OrdinalIgnoreCase)
    {
        "greeting",
        "identity_query",
        "help",
        "context_query",
        "project_research_request",
        "qc_request",
        "family_analysis_request"
    };

    /// <summary>
    /// Returns true if the given intent is conversational or read-only analysis.
    /// These intents do NOT need LLM planning, tool chains, or capability compilation.
    /// They use the ConversationalStep fast-path for 1-3s response instead of 5-18s.
    /// </summary>
    public static bool IsConversationalIntent(string intent)
    {
        return ConversationalIntents.Contains(intent);
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
            DecisionRationale = "Rule-first routing based on clear intent, recent history, and current Revit lane.",
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
            DecisionRationale = "Rule-first routing based on clear intent, recent history, and current Revit lane.",
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
                decision.PlanSummary = "Greet user, gather context, suggest read-only next steps.";
                decision.SuggestedActions.Add(CreateSuggest("Check model health", "Run read-only QC on the current model.", ToolNames.ReviewSmartQc));
                decision.SuggestedActions.Add(CreateSuggest("View current context", "Get document, view, selection and delta summary.", ToolNames.ContextGetDeltaSummary));
                break;

            case "identity_query":
                decision.PlanSummary = "Confirm worker role, open context, and available support lanes in Revit.";
                decision.SuggestedActions.Add(CreateSuggest("View current context", "Get document, view, selection and delta summary.", ToolNames.ContextGetDeltaSummary));
                decision.SuggestedActions.Add(CreateSuggest("Check model health", "Run read-only QC on the current model.", ToolNames.ReviewSmartQc));
                break;

            case "context_query":
                decision.PlanSummary = "Read current context, recent deltas, and suggest next steps.";
                decision.PlannedTools.Add(ToolNames.SessionGetTaskContext);
                decision.PlannedTools.Add(ToolNames.ContextGetDeltaSummary);
                break;

            case "project_research_request":
                decision.PlanSummary = "Grounded research loop: live context -> workspace context bundle -> deep-scan/artifact/standards/memory evidence.";
                decision.PlannedTools.Add(ToolNames.SessionGetTaskContext);
                decision.PlannedTools.Add(ToolNames.ContextGetDeltaSummary);
                decision.PlannedTools.Add(ToolNames.ProjectGetContextBundle);
                decision.PlannedTools.Add(ToolNames.ProjectGetDeepScan);
                decision.PlannedTools.Add(ToolNames.StandardsResolve);
                decision.PlannedTools.Add(ToolNames.ArtifactSummarize);
                decision.PlannedTools.Add(ToolNames.MemorySearchScoped);
                decision.SuggestedActions.Add(CreateSuggest("Grounded project overview", "Summarize current state from live context + workspace bundle + deep scan.", ToolNames.ProjectGetContextBundle));
                break;

            case "sheet_analysis_request":
                decision.PlanSummary = "Analyze sheet with summary + read-only QC, keep MVP lane lean with clear evidence.";
                decision.PlannedTools.Add(ToolNames.ReviewSheetSummary);
                decision.PlannedTools.Add(ToolNames.ReviewSmartQc);
                break;

            case "sheet_authoring_request":
                decision.PlanSummary = "Resolve workspace standards + playbook first, then preview sheet/view/place/QC tool chain.";
                decision.PlannedTools.Add(ToolNames.WorkspaceGetManifest);
                decision.PlannedTools.Add(ToolNames.StandardsResolve);
                decision.PlannedTools.Add(ToolNames.PlaybookMatch);
                decision.PlannedTools.Add(ToolNames.PlaybookPreview);
                decision.SuggestedActions.Add(CreateSuggest("Preview playbook sheet", "Resolve team standards and sheet creation chain before mutation.", ToolNames.PlaybookPreview));
                break;

            case "view_authoring_request":
                decision.PlanSummary = "Atlas fast-path first: quick-plan -> command.execute_safe; fallback to playbook if needed.";
                decision.PlannedTools.Add(ToolNames.WorkflowQuickPlan);
                decision.PlannedTools.Add(ToolNames.CommandDescribe);
                decision.PlannedTools.Add(ToolNames.CommandExecuteSafe);
                decision.SuggestedActions.Add(CreateSuggest("Quick view action", "Resolve create/duplicate/apply-template from command atlas.", ToolNames.WorkflowQuickPlan));
                break;

            case "documentation_request":
                decision.PlanSummary = "Resolve documentation quick-path first, fallback to playbook sheet package for multi-step tasks.";
                decision.PlannedTools.Add(ToolNames.WorkflowQuickPlan);
                decision.PlannedTools.Add(ToolNames.CommandExecuteSafe);
                decision.PlannedTools.Add(ToolNames.PlaybookPreview);
                break;

            case "model_manage_request":
                decision.PlanSummary = "Route to model-manage quick-path for preview purge/cleanup/rename, keep approval gate for mutations.";
                decision.PlannedTools.Add(ToolNames.WorkflowQuickPlan);
                decision.PlannedTools.Add(ToolNames.CommandExecuteSafe);
                decision.PlannedTools.Add(ToolNames.CommandCoverageReport);
                break;

            case "command_palette_request":
                decision.PlanSummary = "Command-palette lookup: search -> describe -> quick-plan -> execute if safe.";
                decision.PlannedTools.Add(ToolNames.CommandSearch);
                decision.PlannedTools.Add(ToolNames.CommandDescribe);
                decision.PlannedTools.Add(ToolNames.WorkflowQuickPlan);
                break;

            case "element_authoring_request":
                decision.PlanSummary = "Atlas quick-path first for atomic authoring; escalate to clarify/playbook if no safe lane exists.";
                decision.PlannedTools.Add(ToolNames.CommandSearch);
                decision.PlannedTools.Add(ToolNames.WorkflowQuickPlan);
                decision.PlannedTools.Add(ToolNames.CommandExecuteSafe);
                break;

            case "governance_request":
                decision.PlanSummary = "Resolve governance domain, policy packs, playbook, and specialist before selecting leaf tools.";
                decision.PlannedTools.Add(ToolNames.ToolGetGuidance);
                decision.PlannedTools.Add(ToolNames.PlaybookMatch);
                decision.PlannedTools.Add(ToolNames.PolicyResolve);
                decision.PlannedTools.Add(ToolNames.SpecialistResolve);
                decision.PlannedTools.Add(ToolNames.IntentCompile);
                decision.SuggestedActions.Add(CreateSuggest("Governance compile", "Compile governance task from policy + workspace playbook.", ToolNames.IntentCompile));
                break;

            case "annotation_request":
                decision.PlanSummary = "Use annotation/governance playbook and policy packs to plan tag/dim/room-finish safely.";
                decision.PlannedTools.Add(ToolNames.ToolGetGuidance);
                decision.PlannedTools.Add(ToolNames.PlaybookMatch);
                decision.PlannedTools.Add(ToolNames.PolicyResolve);
                decision.PlannedTools.Add(ToolNames.IntentCompile);
                break;

            case "coordination_request":
                decision.PlanSummary = "Coordination lane: guidance -> playbook -> policy -> specialist -> compiled fix/verify plan.";
                decision.PlannedTools.Add(ToolNames.ToolGetGuidance);
                decision.PlannedTools.Add(ToolNames.PlaybookMatch);
                decision.PlannedTools.Add(ToolNames.PolicyResolve);
                decision.PlannedTools.Add(ToolNames.SpecialistResolve);
                decision.PlannedTools.Add(ToolNames.IntentCompile);
                decision.SuggestedActions.Add(CreateSuggest("Preview clash plan", "Compile clash/opening task before entering fix loop.", ToolNames.IntentCompile));
                break;

            case "systems_request":
                decision.PlanSummary = "Systems lane: capture graph, compile task, plan disconnected/slope/routing fixes.";
                decision.PlannedTools.Add(ToolNames.ToolGetGuidance);
                decision.PlannedTools.Add(ToolNames.SystemCaptureGraph);
                decision.PlannedTools.Add(ToolNames.PolicyResolve);
                decision.PlannedTools.Add(ToolNames.SpecialistResolve);
                decision.PlannedTools.Add(ToolNames.IntentCompile);
                decision.SuggestedActions.Add(CreateSuggest("Capture system graph", "Snapshot connectivity/slope before planning fix.", ToolNames.SystemCaptureGraph));
                break;

            case "integration_request":
                decision.PlanSummary = "Integration lane: compile task, preview sync delta, keep cloud/plugin-safe boundary.";
                decision.PlannedTools.Add(ToolNames.ToolGetGuidance);
                decision.PlannedTools.Add(ToolNames.IntentCompile);
                decision.PlannedTools.Add(ToolNames.IntegrationPreviewSync);
                decision.PlannedTools.Add(ToolNames.PolicyResolve);
                break;

            case "intent_compile_request":
                decision.PlanSummary = "Compile natural-language request into typed task plan, validate tool lanes, then propose execute.";
                decision.PlannedTools.Add(ToolNames.IntentCompile);
                decision.PlannedTools.Add(ToolNames.IntentValidate);
                decision.PlannedTools.Add(ToolNames.ToolGetGuidance);
                break;

            case "family_analysis_request":
                decision.PlanSummary = "Analyze current or specified family using X-Ray.";
                decision.PlannedTools.Add(ToolNames.FamilyXray);
                break;

            case "qc_request":
                decision.PlanSummary = "Run model QC read-only, then summarize findings and next actions.";
                decision.PlannedTools.Add(ToolNames.ReviewModelHealth);
                decision.PlannedTools.Add(ToolNames.ReviewSmartQc);
                break;

            case "mutation_request":
                decision.PlanSummary = "No direct mutation. Will preview or request scope clarification first.";
                decision.SuggestedActions.Add(new WorkerActionCard
                {
                    ActionKind = WorkerActionKinds.Clarify,
                    Title = "Clarify mutation scope",
                    Summary = "Specify the element/scope/tool you want, e.g. purge unused or fill parameter.",
                    IsPrimary = true,
                    ExecutionTier = WorkerExecutionTiers.Tier1,
                    WhyThisAction = "Mutation lane requires clear scope before worker proposes a safe tool.",
                    Confidence = 0.8d,
                    RecoveryHint = "To mutate now, specify the family, element, parameter, view, or workflow.",
                    AutoExecutionEligible = false
                });
                break;

            case "approval":
                decision.PlanSummary = "Approval pending — validate token and execute safely via kernel.";
                decision.PlannedTools.Add("pending_approval");
                break;

            case "reject":
                decision.PlanSummary = "Cancel pending approval and set mission to blocked state.";
                break;

            case "resume":
                decision.PlanSummary = "Resume most recent mission if state and context are still valid.";
                decision.PlannedTools.Add("resume_mission");
                break;

            case "cancel":
                decision.PlanSummary = "End current mission, no further execution.";
                break;

            default:
                decision.RequiresClarification = true;
                decision.PlanSummary = "Intent not confidently mapped. Will ask for clarification and suggest safe tools.";
                decision.SuggestedActions.Add(CreateSuggest("Check model health", "Run read-only QC on the current model.", ToolNames.ReviewSmartQc));
                decision.SuggestedActions.Add(CreateSuggest("Get context", "Read active doc/view/selection and delta.", ToolNames.ContextGetDeltaSummary));
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
            WhyThisAction = "Suggestion routed by intent + persona + current Revit lane.",
            Confidence = 0.86d,
            RecoveryHint = "If suggestion does not match scope, re-describe the target document/view/selection.",
            AutoExecutionEligible = false
        };
    }

    private static string BuildResponseLead(string intent)
    {
        if (string.Equals(intent, "greeting", StringComparison.OrdinalIgnoreCase))
        {
            return "Syncing Revit context and ready for the next task.";
        }

        if (string.Equals(intent, "identity_query", StringComparison.OrdinalIgnoreCase))
        {
            return "Confirming worker role and open context so you know what I can do right now.";
        }

        return "Building a safe plan before selecting tool or preview.";
    }

    private static string BuildGoal(string intent, string targetHint)
    {
        switch (intent)
        {
            case "context_query":
                return "Understand the current Revit context before acting.";
            case "identity_query":
                return "Introduce worker role and capabilities in the open model.";
            case "project_research_request":
                return "Summarize project state from live Revit context, workspace context bundle, and deep-scan evidence if available.";
            case "sheet_analysis_request":
                return string.IsNullOrWhiteSpace(targetHint) ? "Analyze the current sheet." : $"Analyze sheet {targetHint}.";
            case "sheet_authoring_request":
                return string.IsNullOrWhiteSpace(targetHint) ? "Create sheet per workspace standards." : $"Create/preview sheet {targetHint} per workspace standards.";
            case "view_authoring_request":
                return "Execute safe, context-aware quick action for view; fallback to playbook if needed.";
            case "documentation_request":
                return "Execute documentation quick-path or package workflow depending on complexity.";
            case "model_manage_request":
                return "Preview and execute manage/cleanup commands via safe quick-path.";
            case "command_palette_request":
                return "Resolve command from atlas, not tool-hunt via inflated context.";
            case "element_authoring_request":
                return "Try atomic authoring via atlas fast-path before entering large workflow.";
            case "governance_request":
                return "Resolve governance standards, playbooks, and fix lanes per workspace.";
            case "annotation_request":
                return "Plan annotation/room-finish task per policy and playbook safely.";
            case "coordination_request":
                return "Build coordination plan for clash/clearance/opening with preview + verify.";
            case "systems_request":
                return "Build systems plan for connectivity/slope/routing with graph snapshot first.";
            case "integration_request":
                return "Preview integration delta and keep external sync in connector-safe lane.";
            case "intent_compile_request":
                return "Compile natural-language request into a typed, validatable plan.";
            case "family_analysis_request":
                return string.IsNullOrWhiteSpace(targetHint) ? "Analyze the current family." : $"Analyze family {targetHint}.";
            case "qc_request":
                return "QC the current model in read-only mode.";
            case "mutation_request":
                return "Build safe mutation plan, preview before execute.";
            case "approval":
                return "Execute the approved preview.";
            case "reject":
                return "Safely cancel pending approval.";
            case "resume":
                return "Resume the pending mission.";
            case "cancel":
                return "Stop the current mission.";
            default:
                return "Assist worker based on the current Revit context.";
        }
    }
}
