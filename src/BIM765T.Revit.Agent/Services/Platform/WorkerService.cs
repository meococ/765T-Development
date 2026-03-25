using System;

using System.Collections.Generic;

using System.Linq;

using System.Reflection;

using System.Text.RegularExpressions;

using Autodesk.Revit.DB;

using Autodesk.Revit.UI;

using BIM765T.Revit.Agent.Infrastructure;

using BIM765T.Revit.Agent.Infrastructure.Time;

using BIM765T.Revit.Copilot.Core.Brain;

using BIM765T.Revit.Copilot.Core.Memory;

using BIM765T.Revit.Copilot.Core;

using BIM765T.Revit.Contracts.Bridge;

using BIM765T.Revit.Contracts.Common;

using BIM765T.Revit.Contracts.Platform;

using BIM765T.Revit.Contracts.Serialization;



namespace BIM765T.Revit.Agent.Services.Platform;



internal sealed class WorkerService

{

    private readonly PlatformServices _platform;

    private readonly AuditService _audit;

    private readonly SmartQcService _smartQc;

    private readonly ScheduleExtractionService _scheduleExtraction;

    private readonly FamilyXrayService _familyXray;

    private readonly SheetIntelligenceService _sheetIntelligence;

    private readonly CopilotTaskService _copilotTasks;

    private readonly ConversationManager _conversations;

    private readonly MissionCoordinator _missions;

    private readonly WorkerReasoningEngine _reasoning;

    private readonly PersonaRegistry _personas;

    private readonly SessionMemoryStore _sessionMemory;

    private readonly EpisodicMemoryStore _episodicMemory;

    private readonly WorkerPlaybookExecutionService _playbookExecution;

    private readonly CommandAtlasService _commandAtlas;

    private readonly LlmResponseEnhancer? _enhancer;

    private readonly ISystemClock _clock;



    internal WorkerService(

        PlatformServices platform,

        AuditService audit,

        SmartQcService smartQc,

        ScheduleExtractionService scheduleExtraction,

        FamilyXrayService familyXray,

        SheetIntelligenceService sheetIntelligence,

        CopilotTaskService copilotTasks,

        ConversationManager conversations,

        MissionCoordinator missions,

        WorkerReasoningEngine reasoning,

        PersonaRegistry personas,

        SessionMemoryStore sessionMemory,

        EpisodicMemoryStore episodicMemory,

        WorkerPlaybookExecutionService playbookExecution,

        ISystemClock clock,

        LlmResponseEnhancer? enhancer = null)

    {

        _platform = platform;

        _audit = audit;

        _smartQc = smartQc;

        _scheduleExtraction = scheduleExtraction;

        _familyXray = familyXray;

        _sheetIntelligence = sheetIntelligence;

        _copilotTasks = copilotTasks;

        _conversations = conversations;

        _missions = missions;

        _reasoning = reasoning;

        _personas = personas;

        _sessionMemory = sessionMemory;

        _episodicMemory = episodicMemory;

        _playbookExecution = playbookExecution;

        _commandAtlas = new CommandAtlasService();

        _enhancer = enhancer;

        _clock = clock;

    }



    internal WorkerResponse HandleMessage(UIApplication uiapp, ToolRequestEnvelope envelope, WorkerMessageRequest request)

    {

        request ??= new WorkerMessageRequest();

        var doc = ResolveDocument(uiapp, envelope);

        var documentKey = _platform.GetDocumentKey(doc);

        var session = _conversations.GetOrCreateSession(

            request.SessionId,

            ResolvePersonaId(request.PersonaId),

            string.IsNullOrWhiteSpace(request.ClientSurface) ? WorkerClientSurfaces.Ui : request.ClientSurface,

            documentKey);

        var workspaceId = ResolveWorkspaceId(doc);

        var contextSummary = BuildContextSummary(uiapp, doc, request.Message, string.Empty);



        var userMessage = CreateMessage(WorkerMessageRoles.User, request.Message);

        _conversations.AddMessage(session.SessionId, userMessage);

        CaptureSessionMemory(session, WorkerMemoryKinds.UserMessage, request.Message, doc, string.Empty, "user", "message");



        var decision = _reasoning.ProcessMessage(session, request.Message, request.ContinueMission, contextSummary, workspaceId);
        ApplyPlannerHints(request, decision, contextSummary);

        _missions.EnsureMission(session, decision.Intent, decision.Goal, request.ContinueMission);

        _missions.SetPlan(session, decision);
        session.Mission.AutonomyMode = WorkerAutonomyModes.Normalize(request.AutonomyMode);
        session.Mission.PlannerTraceSummary = FirstNonEmpty(request.PlannerTraceSummary, session.Mission.DecisionRationale);
        session.Mission.ChosenToolSequence = request.ChosenToolSequence?
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? new List<string>();



        var toolCards = new List<WorkerToolCard>();

        var actionCards = new List<WorkerActionCard>(decision.SuggestedActions);
        AddPlannerHintActionCards(actionCards, request, decision);

        var artifactRefs = new List<string>();

        contextSummary = BuildContextSummary(uiapp, doc, request.Message, decision.Intent);

        var pendingApproval = session.PendingApprovalState;

        var approvalCheckpoint = pendingApproval != null && pendingApproval.HasPendingApproval && IsApprovalCheckpointIntent(decision.Intent);

        var deferForApproval = pendingApproval != null && pendingApproval.HasPendingApproval && !approvalCheckpoint;



        var selectedPlaybook = new PlaybookRecommendation();

        var playbookPreview = new PlaybookPreviewResponse

        {

            WorkspaceId = workspaceId,

            Standards = new StandardsResolution { WorkspaceId = workspaceId }

        };

        var capabilityDomain = CapabilityDomains.General;

        var compiledPlan = new CompiledTaskPlan

        {

            CapabilityDomain = capabilityDomain,

            VerificationMode = ToolVerificationModes.ReportOnly,

            Summary = deferForApproval && pendingApproval != null

                ? $"Approval pending: {pendingApproval.ToolName}"

                : string.Empty

        };

        session.Mission.WorkspaceId = workspaceId;



        if (!deferForApproval && ShouldResolveCapabilityPlanning(decision.Intent))

        {

            var playbookMatch = _copilotTasks.MatchPlaybook(

                GetToolCatalog(),

                new PlaybookMatchRequest

                {

                    WorkspaceId = workspaceId,

                    Query = request.Message,

                    DocumentContext = ResolveDocumentContext(doc, decision.Intent),

                    MaxResults = 3

                });

            selectedPlaybook = playbookMatch.RecommendedPlaybook ?? new PlaybookRecommendation();

            playbookPreview = !string.IsNullOrWhiteSpace(selectedPlaybook.PlaybookId)

                ? _copilotTasks.PreviewPlaybook(

                    GetToolCatalog(),

                    new PlaybookPreviewRequest

                    {

                        WorkspaceId = workspaceId,

                        PlaybookId = selectedPlaybook.PlaybookId,

                        Query = request.Message,

                        DocumentContext = ResolveDocumentContext(doc, decision.Intent)

                    })

                : playbookPreview;

            capabilityDomain = ResolveCapabilityDomain(decision.Intent, selectedPlaybook, playbookPreview);

            compiledPlan = _copilotTasks.CompileIntentPlan(

                GetToolCatalog(),

                new IntentCompileRequest

                {

                    PreferredCapabilityDomain = capabilityDomain,

                    Discipline = ResolveDiscipline(request.Message, decision.Intent),

                    RequireDeterministicPlan = !string.Equals(capabilityDomain, CapabilityDomains.Integration, StringComparison.OrdinalIgnoreCase),

                    Task = new IntentTask

                    {

                        Query = request.Message,

                        WorkspaceId = workspaceId,

                        DocumentContext = ResolveDocumentContext(doc, decision.Intent),

                        CapabilityDomain = capabilityDomain,

                        Discipline = ResolveDiscipline(request.Message, decision.Intent),

                        RequestedOutcome = decision.Goal,

                        IssueKinds = ResolveIssueKinds(request.Message, capabilityDomain, decision.Intent)

                    }

                });



            session.Mission.SelectedPlaybookId = selectedPlaybook.PlaybookId ?? string.Empty;

            session.Mission.CapabilityDomain = string.IsNullOrWhiteSpace(compiledPlan.CapabilityDomain) ? capabilityDomain : compiledPlan.CapabilityDomain;

            session.Mission.PolicySummary = compiledPlan.PolicyResolution?.Summary ?? string.Empty;

            session.Mission.RecommendedSpecialistIds = (compiledPlan.RecommendedSpecialists ?? new List<CapabilitySpecialistDescriptor>())

                .Select(x => x.SpecialistId)

                .Where(x => !string.IsNullOrWhiteSpace(x))

                .Distinct(StringComparer.OrdinalIgnoreCase)

                .ToList();

            if (!string.IsNullOrWhiteSpace(selectedPlaybook.PlaybookId))

            {

                session.Mission.PlanSummary = AppendPlaybookSummary(session.Mission.PlanSummary, selectedPlaybook, playbookPreview);

                session.Mission.ReasoningSummary = AppendPlaybookReasoning(session.Mission.ReasoningSummary, selectedPlaybook, playbookPreview);

                actionCards.Insert(0, new WorkerActionCard

                {

                    ActionKind = WorkerActionKinds.Suggest,

                    Title = "Playbook match",

                    Summary = $"{selectedPlaybook.PlaybookId}: {playbookPreview.Summary}",

                    ToolName = ToolNames.PlaybookPreview,

                    IsPrimary = actionCards.Count == 0,

                    ExecutionTier = WorkerExecutionTiers.Tier0,

                    WhyThisAction = "Worker uu tien one orchestrator + playbook chain truoc khi fallback sang tool hunt.",

                    Confidence = Math.Max(0.75d, selectedPlaybook.Confidence),

                    RecoveryHint = "Neu standards hoac inputs chua du, call standards.resolve hoac preview lai playbook.",

                    AutoExecutionEligible = false

                });

            }

            if (!string.IsNullOrWhiteSpace(compiledPlan.Summary))

            {

                actionCards.Add(new WorkerActionCard

                {

                    ActionKind = WorkerActionKinds.Context,

                    Title = "Capability plan",

                    Summary = compiledPlan.Summary,

                    ToolName = ToolNames.IntentCompile,

                    IsPrimary = actionCards.Count == 0,

                    ExecutionTier = WorkerExecutionTiers.Tier0,

                    WhyThisAction = "Worker compile task vao capability-centric plan truoc khi execute leaf tools.",

                    Confidence = 0.84d,

                    RecoveryHint = "Neu scope chua dung, noi ro discipline/issue kind hoac workspace policy can dung.",

                    AutoExecutionEligible = false

                });

            }

        }

        else

        {

            actionCards.Clear();

            AppendApprovalCheckpointActions(actionCards, pendingApproval!);

            _missions.AwaitApproval(session, pendingApproval!.Summary, new[] { pendingApproval.ToolName });

        }



        string responseText;

        if (deferForApproval)

        {

            responseText = BuildPendingApprovalCheckpointResponse(pendingApproval!, decision.Intent);

        }

        else

        {

            switch (decision.Intent)

            {

                case "greeting":

                    responseText = BuildGreetingResponseText(contextSummary);

                    _missions.Complete(session, "Worker đã sẵn sàng cho nhiệm vụ tiếp theo.");

                    break;

                case "identity_query":

                    responseText = BuildIdentityResponseText(contextSummary);

                    _missions.Complete(session, "Worker đã giới thiệu vai trò và lane hỗ trợ hiện tại.");

                    break;

                case "help":

                    responseText = BuildHelpResponseText(contextSummary);

                    _missions.Complete(session, "Worker đã gợi ý các điểm vào an toàn.");

                    break;

                case "context_query":

                case "project_research_request":

                    responseText = RunGroundedProjectResearch(

                        uiapp,

                        doc,

                        request.Message,

                        decision.Intent,

                        workspaceId,

                        toolCards,

                        artifactRefs,

                        out contextSummary);

                    _missions.Complete(session, "Da tong hop grounded project research.");

                    break;

                case "qc_request":

                    RunQc(uiapp, doc, request.Message, toolCards, artifactRefs, out responseText);

                    _missions.Complete(session, "QC read-only da hoan tat.");

                    break;

                case "sheet_analysis_request":

                    RunSheetAnalysis(uiapp, doc, request.Message, toolCards, artifactRefs, out responseText);

                    _missions.Complete(session, "Sheet analysis da hoan tat.");

                    break;

                case "sheet_authoring_request":

                    responseText = HandleSheetAuthoringIntent(uiapp, doc, envelope, request.Message, session, selectedPlaybook, playbookPreview, actionCards, toolCards);

                    break;

                case "view_authoring_request":

                case "documentation_request":

                case "model_manage_request":

                case "command_palette_request":

                case "element_authoring_request":

                    responseText = HandleQuickPathIntent(uiapp, doc, envelope, request.Message, decision, session, toolCards, actionCards, artifactRefs);

                    break;

                case "governance_request":

                case "annotation_request":

                case "coordination_request":

                case "systems_request":

                case "integration_request":

                case "intent_compile_request":

                    responseText = HandleCapabilityIntent(decision.Intent, compiledPlan, actionCards, toolCards);

                    _missions.Complete(session, $"Capability plan {compiledPlan.CapabilityDomain} da san sang.");

                    break;

                case "family_analysis_request":

                    RunFamilyAnalysis(uiapp, doc, request.Message, toolCards, out responseText);

                    _missions.Complete(session, "Family analysis da hoan tat.");

                    break;

                case "mutation_request":

                    responseText = HandleMutationIntent(uiapp, doc, envelope, request.Message, session, toolCards, actionCards);

                    break;

                case "approval":

                    responseText = ApprovePending(uiapp, doc, envelope, session, toolCards, artifactRefs, actionCards);

                    break;

                case "reject":

                    responseText = RejectPending(session);

                    break;

                case "cancel":

                    responseText = CancelMission(session);

                    break;

                case "resume":

                    responseText = ResumeMission(session, actionCards);

                    break;

                default:

                    responseText = "Em chưa xác định chắc ý định từ câu này. Anh thử nói rõ hơn như QC model, QC sheet, phân tích family, hoặc xem context hiện tại.";

                    _missions.Block(session, "Intent cần được làm rõ thêm.", "Worker rule-first chưa đủ tín hiệu để chọn tool an toàn.");

                    break;

            }

        }



        if (!string.IsNullOrWhiteSpace(selectedPlaybook.PlaybookId)

            && !string.Equals(decision.Intent, "greeting", StringComparison.OrdinalIgnoreCase)

            && !string.Equals(decision.Intent, "help", StringComparison.OrdinalIgnoreCase)

            && !string.Equals(decision.Intent, "view_authoring_request", StringComparison.OrdinalIgnoreCase)

            && !string.Equals(decision.Intent, "documentation_request", StringComparison.OrdinalIgnoreCase)

            && !string.Equals(decision.Intent, "command_palette_request", StringComparison.OrdinalIgnoreCase)

            && !string.Equals(decision.Intent, "model_manage_request", StringComparison.OrdinalIgnoreCase)

            && !string.Equals(decision.Intent, "element_authoring_request", StringComparison.OrdinalIgnoreCase))

        {

            responseText = $"Playbook {selectedPlaybook.PlaybookId}: {playbookPreview.Summary} {responseText}".Trim();

        }



        // ── LLM text enhancement (Phase 1) ─────────────────────────

        // Tool selection is complete. Enhance display text using LLM if configured.

        // Fallback: returns the rule-based text unchanged if LLM is unavailable.

        var narrationMode = WorkerNarrationModes.RuleOnly;

        var narrationDiagnostics = "Dang dung response rule-based trong native worker lane.";

        if (_enhancer != null)

        {

            if (_enhancer.IsLlmConfigured)

            {

                var persona = _personas.Resolve(session.PersonaId);

                var toolSummaries = toolCards.Select(c => $"{c.ToolName}: {c.Summary}");

                var narration = _enhancer.EnhanceResponse(

                    request.Message,

                    decision.Intent,

                    responseText,

                    toolSummaries,

                    contextSummary,

                    persona,

                    session.Messages,

                    session.Mission.ReasoningSummary,

                    session.Mission.PlanSummary);

                responseText = narration.Text;

                narrationMode = narration.Mode;

                narrationDiagnostics = narration.Diagnostics;

            }

            else

            {

                narrationDiagnostics = "LLM narration client khong duoc cau hinh trong runtime add-in.";

            }

        }

        // ────────────────────────────────────────────────────────────



        if (!string.Equals(decision.Intent, "context_query", StringComparison.OrdinalIgnoreCase)

            && !string.Equals(decision.Intent, "project_research_request", StringComparison.OrdinalIgnoreCase))

        {

            contextSummary = BuildContextSummary(uiapp, doc, request.Message, decision.Intent);

        }

        var queueState = GetQueueState();

        contextSummary.QueueSummary = BuildQueueSummary(queueState);

        contextSummary.WorkspaceId = workspaceId;

        contextSummary.PackSummary = BuildPackSummary(selectedPlaybook, playbookPreview);

        _conversations.UpdateContextSummary(session.SessionId, contextSummary);



        var workerMessage = CreateMessage(WorkerMessageRoles.Worker, responseText);

        _conversations.AddMessage(session.SessionId, workerMessage);

        CaptureSessionMemory(session, WorkerMemoryKinds.WorkerResponse, responseText, doc, string.Empty, "worker", decision.Intent);



        foreach (var card in toolCards)

        {

            CaptureSessionMemory(session, WorkerMemoryKinds.ToolResult, card.Summary, doc, card.ToolName, "tool", card.ToolName);

        }



        if (ShouldPersistEpisode(session))

        {

            PersistEpisode(session, toolCards, artifactRefs, responseText);

        }



        var normalizedToolCards = NormalizeToolCards(toolCards);

        var pendingApprovalRef = ToPendingApprovalRef(session.PendingApprovalState);

        var stage = ResolveStage(session.Mission.Status);

        var progress = ResolveMissionProgress(session.Mission.Status, session.PendingApprovalState, queueState);

        var confidence = ResolveConfidence(decision.Intent, normalizedToolCards, actionCards);

        var recoveryHints = BuildRecoveryHints(contextSummary, normalizedToolCards, session);

        var executionTier = ResolveExecutionTier(normalizedToolCards, actionCards, session.PendingApprovalState);

        var autoExecutionEligible = normalizedToolCards.Any(x => x.AutoExecutionEligible) || actionCards.Any(x => x.AutoExecutionEligible);

        var contextPills = BuildContextPills(contextSummary, session, executionTier);

        var executionItems = BuildExecutionItems(normalizedToolCards, session.PendingApprovalState);

        var evidenceItems = BuildEvidenceItems(artifactRefs, normalizedToolCards, compiledPlan);

        var riskSummary = BuildRiskSummary(session.PendingApprovalState, executionTier, normalizedToolCards, compiledPlan, artifactRefs);

        var surfaceHint = ResolveSurfaceHint(session, artifactRefs, normalizedToolCards, actionCards);

        var projectBundle = GetProjectBundle(workspaceId, request.Message, 6, 6);

        var fallbackProposal = ResolveFallbackProposal(normalizedToolCards);

        var skillCaptureProposal = BuildSkillCaptureProposal(session, workspaceId, selectedPlaybook, artifactRefs, normalizedToolCards, executionTier);

        var projectPatternSnapshot = BuildProjectPatternSnapshot(projectBundle, selectedPlaybook, compiledPlan, normalizedToolCards, ResolveDiscipline(request.Message, decision.Intent), workspaceId);

        var templateSynthesisProposal = BuildTemplateSynthesisProposal(projectBundle, projectPatternSnapshot);

        var deltaSuggestions = BuildDeltaSuggestions(projectBundle, contextSummary, pendingApprovalRef, normalizedToolCards, selectedPlaybook, workspaceId);

        AppendSkillCaptureActionCard(actionCards, skillCaptureProposal);

        var suggestedCommands = BuildSuggestedCommands(actionCards, contextSummary);

        var onboardingStatus = BuildOnboardingStatus(session, contextSummary, pendingApprovalRef);

        session.Mission.Stage = stage;

        session.Mission.Confidence = confidence;



        return new WorkerResponse

        {

            SessionId = session.SessionId,

            MissionId = session.Mission.MissionId,

            MissionStatus = session.Mission.Status,

            Messages = session.Messages.ToList(),

            ActionCards = actionCards,

            PendingApproval = pendingApprovalRef,

            ToolCards = normalizedToolCards,

            ArtifactRefs = artifactRefs.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),

            ContextSummary = contextSummary,

            ReasoningSummary = session.Mission.ReasoningSummary,

            PlanSummary = session.Mission.PlanSummary,

            Stage = stage,

            Progress = progress,

            Confidence = confidence,

            RecoveryHints = recoveryHints,

            ExecutionTier = executionTier,

            AutoExecutionEligible = autoExecutionEligible,

            QueueState = queueState,

            WorkspaceId = workspaceId,

            SelectedPlaybook = selectedPlaybook,

            PlaybookPreview = playbookPreview,

            StandardsSummary = playbookPreview.Standards?.Summary ?? string.Empty

            ,

            ResolvedCapabilityDomain = session.Mission.CapabilityDomain,

            PolicySummary = session.Mission.PolicySummary,

            RecommendedSpecialists = compiledPlan.RecommendedSpecialists?.ToList() ?? new List<CapabilitySpecialistDescriptor>(),

            CompiledPlan = compiledPlan,

            ContextPills = contextPills,

            ExecutionItems = executionItems,

            EvidenceItems = evidenceItems,

            SuggestedCommands = suggestedCommands,

            PrimaryRiskSummary = riskSummary,

            SurfaceHint = surfaceHint,

            OnboardingStatus = onboardingStatus,

            FallbackProposal = fallbackProposal,

            SkillCaptureProposal = skillCaptureProposal,

            ProjectPatternSnapshot = projectPatternSnapshot,

            TemplateSynthesisProposal = templateSynthesisProposal,

            DeltaSuggestions = deltaSuggestions,

            ConfiguredProvider = decision.ConfiguredProvider,

            PlannerModel = decision.PlannerModel,

            ResponseModel = decision.ResponseModel,

            ReasoningMode = decision.ReasoningMode,

            NarrationMode = narrationMode,

            NarrationDiagnostics = narrationDiagnostics,

            AutonomyMode = session.Mission.AutonomyMode,

            PlannerTraceSummary = session.Mission.PlannerTraceSummary,

            ChosenToolSequence = session.Mission.ChosenToolSequence.ToList()

        };

    }



    internal WorkerResponse GetSession(WorkerSessionRequest request)

    {

        var session = _conversations.GetRequiredSession(request.SessionId);

        var queueState = GetQueueState();

        var stage = ResolveStage(session.Mission.Status);

        var contextSummary = session.ContextSummary ?? new WorkerContextSummary();

        contextSummary.QueueSummary = BuildQueueSummary(queueState);

        contextSummary.WorkspaceId = string.IsNullOrWhiteSpace(contextSummary.WorkspaceId) ? session.Mission.WorkspaceId : contextSummary.WorkspaceId;

        var recoveryHints = BuildRecoveryHints(contextSummary, Enumerable.Empty<WorkerToolCard>(), session);

        var executionTier = ResolveExecutionTier(Enumerable.Empty<WorkerToolCard>(), Enumerable.Empty<WorkerActionCard>(), session.PendingApprovalState);

        var playbookPreview = !string.IsNullOrWhiteSpace(session.Mission.SelectedPlaybookId)

            ? _copilotTasks.PreviewPlaybook(

                GetToolCatalog(),

                new PlaybookPreviewRequest

                {

                    WorkspaceId = session.Mission.WorkspaceId,

                    PlaybookId = session.Mission.SelectedPlaybookId,

                    DocumentContext = "project"

                })

            : new PlaybookPreviewResponse

            {

                WorkspaceId = session.Mission.WorkspaceId,

                Standards = new StandardsResolution { WorkspaceId = session.Mission.WorkspaceId }

            };

        var compiledPlan = new CompiledTaskPlan

        {

            CapabilityDomain = session.Mission.CapabilityDomain,

            Summary = session.Mission.PolicySummary,

            VerificationMode = playbookPreview.VerificationMode

        };

        var pendingApprovalRef = ToPendingApprovalRef(session.PendingApprovalState);

        var projectBundle = GetProjectBundle(session.Mission.WorkspaceId, string.Empty, 6, 6);

        var skillCaptureProposal = BuildSkillCaptureProposal(

            session,

            session.Mission.WorkspaceId,

            new PlaybookRecommendation

            {

                PlaybookId = session.Mission.SelectedPlaybookId,

                Confidence = session.Mission.Confidence

            },

            Enumerable.Empty<string>(),

            Enumerable.Empty<WorkerToolCard>(),

            executionTier);

        var projectPatternSnapshot = BuildProjectPatternSnapshot(

            projectBundle,

            new PlaybookRecommendation

            {

                PlaybookId = session.Mission.SelectedPlaybookId,

                Confidence = session.Mission.Confidence

            },

            compiledPlan,

            Enumerable.Empty<WorkerToolCard>(),

            CapabilityDisciplines.Common,

            session.Mission.WorkspaceId);

        var templateSynthesisProposal = BuildTemplateSynthesisProposal(projectBundle, projectPatternSnapshot);

        var deltaSuggestions = BuildDeltaSuggestions(projectBundle, contextSummary, pendingApprovalRef, Enumerable.Empty<WorkerToolCard>(), new PlaybookRecommendation

        {

            PlaybookId = session.Mission.SelectedPlaybookId,

            Confidence = session.Mission.Confidence

        }, session.Mission.WorkspaceId);

        var onboardingStatus = BuildOnboardingStatus(session, contextSummary, pendingApprovalRef);



        return new WorkerResponse

        {

            SessionId = session.SessionId,

            MissionId = session.Mission.MissionId,

            MissionStatus = session.Mission.Status,

            Messages = session.Messages.ToList(),

            PendingApproval = pendingApprovalRef,

            ContextSummary = contextSummary,

            ReasoningSummary = session.Mission.ReasoningSummary,

            PlanSummary = session.Mission.PlanSummary,

            Stage = stage,

            Progress = ResolveMissionProgress(session.Mission.Status, session.PendingApprovalState, queueState),

            Confidence = session.Mission.Confidence,

            RecoveryHints = recoveryHints,

            ExecutionTier = executionTier,

            AutoExecutionEligible = session.PendingApprovalState?.AutoExecutionEligible ?? false,

            QueueState = queueState,

            WorkspaceId = session.Mission.WorkspaceId,

            SelectedPlaybook = new PlaybookRecommendation

            {

                PlaybookId = session.Mission.SelectedPlaybookId,

                Confidence = session.Mission.Confidence

            },

            PlaybookPreview = playbookPreview,

            StandardsSummary = playbookPreview.Standards?.Summary ?? string.Empty,

            ResolvedCapabilityDomain = session.Mission.CapabilityDomain,

            PolicySummary = session.Mission.PolicySummary,

            RecommendedSpecialists = new List<CapabilitySpecialistDescriptor>(),

            CompiledPlan = compiledPlan,

            ContextPills = BuildContextPills(contextSummary, session, executionTier),

            ExecutionItems = BuildExecutionItems(Enumerable.Empty<WorkerToolCard>(), session.PendingApprovalState),

            EvidenceItems = BuildEvidenceItems(Enumerable.Empty<string>(), Enumerable.Empty<WorkerToolCard>(), compiledPlan),

            SuggestedCommands = BuildSuggestedCommands(Enumerable.Empty<WorkerActionCard>(), contextSummary),

            PrimaryRiskSummary = BuildRiskSummary(session.PendingApprovalState, executionTier, Enumerable.Empty<WorkerToolCard>(), compiledPlan, Enumerable.Empty<string>()),

            SurfaceHint = ResolveSurfaceHint(session, Enumerable.Empty<string>(), Enumerable.Empty<WorkerToolCard>(), Enumerable.Empty<WorkerActionCard>()),

            OnboardingStatus = onboardingStatus,

            SkillCaptureProposal = skillCaptureProposal,

            ProjectPatternSnapshot = projectPatternSnapshot,

            TemplateSynthesisProposal = templateSynthesisProposal,

            DeltaSuggestions = deltaSuggestions,

            ConfiguredProvider = session.Mission.ConfiguredProvider,

            PlannerModel = session.Mission.PlannerModel,

            ResponseModel = session.Mission.ResponseModel,

            ReasoningMode = string.IsNullOrWhiteSpace(session.Mission.ReasoningMode)

                ? WorkerReasoningModes.RuleFirst

                : session.Mission.ReasoningMode,

            AutonomyMode = session.Mission.AutonomyMode,

            PlannerTraceSummary = session.Mission.PlannerTraceSummary,

            ChosenToolSequence = session.Mission.ChosenToolSequence.ToList()

        };

    }



    internal List<WorkerSessionSummary> ListSessions(WorkerListSessionsRequest request)

    {

        request ??= new WorkerListSessionsRequest();

        return _conversations.ListSessions(request.MaxResults, request.IncludeEnded).ToList();

    }



    internal WorkerSessionSummary EndSession(WorkerSessionRequest request)

    {

        return _conversations.EndSession(request.SessionId);

    }



    internal WorkerSessionSummary SetPersona(WorkerSetPersonaRequest request)

    {

        var persona = _personas.Resolve(request.PersonaId);

        return _conversations.SetPersona(request.SessionId, persona.PersonaId);

    }



    internal List<WorkerPersonaSummary> ListPersonas()

    {

        return _personas.List().ToList();

    }



    internal WorkerContextResponse GetContext(UIApplication uiapp, ToolRequestEnvelope envelope, WorkerContextRequest request)

    {

        request ??= new WorkerContextRequest();

        var doc = ResolveDocument(uiapp, envelope);

        var contextSummary = BuildContextSummary(uiapp, doc, string.Empty, "context_query");

        var sessionId = request.SessionId ?? string.Empty;

        var missionId = string.Empty;

        if (!string.IsNullOrWhiteSpace(sessionId) && _conversations.TryGetSession(sessionId, out var session) && session != null)

        {

            missionId = session.Mission.MissionId;

        }



        return new WorkerContextResponse

        {

            SessionId = sessionId,

            MissionId = missionId,

            TaskContext = BuildTaskContext(uiapp, doc, request.MaxRecentOperations, request.MaxRecentEvents),

            DeltaSummary = BuildDeltaSummary(uiapp, doc, request.MaxRecentOperations, request.MaxRecentEvents),

            QueueState = GetQueueState(),

            SimilarEpisodes = contextSummary.SimilarEpisodeHints.ToList(),

            WorkspaceId = contextSummary.WorkspaceId,

            ProjectSummary = contextSummary.ProjectSummary,

            ProjectBrief = _copilotTasks.GetProjectContextBundle(GetToolCatalog(), new ProjectContextBundleRequest

            {

                WorkspaceId = contextSummary.WorkspaceId,

                Query = string.Empty,

                MaxSourceRefs = 4,

                MaxStandardsRefs = 4

            }).ProjectBrief,

            ProjectPrimaryModelStatus = contextSummary.ProjectPrimaryModelStatus,

            ProjectTopRefs = contextSummary.ProjectTopRefs.ToList(),

            ProjectPendingUnknowns = contextSummary.ProjectPendingUnknowns.ToList()

        };

    }



    private string RunGroundedProjectResearch(

        UIApplication uiapp,

        Document doc,

        string query,

        string intent,

        string workspaceId,

        ICollection<WorkerToolCard> toolCards,

        ICollection<string> artifactRefs,

        out WorkerContextSummary contextSummary)

    {

        contextSummary = BuildContextSummary(uiapp, doc, query, intent);

        var taskContext = BuildTaskContext(uiapp, doc, 10, 10);

        var deltaSummary = BuildDeltaSummary(uiapp, doc, 10, 10);

        toolCards.Add(BuildToolCard(

            ToolNames.SessionGetTaskContext,

            StatusCodes.ReadSucceeded,

            true,

            BuildTaskContextSummary(taskContext),

            taskContext,

            stage: WorkerFlowStages.Scan,

            confidence: 0.96d,

            executionTier: WorkerExecutionTiers.Tier0,

            autoExecutionEligible: false));

        toolCards.Add(BuildToolCard(

            ToolNames.ContextGetDeltaSummary,

            StatusCodes.ReadSucceeded,

            true,

            FirstNonEmpty(deltaSummary.Summary, "Da lay delta summary cho active document."),

            deltaSummary,

            stage: WorkerFlowStages.Scan,

            confidence: 0.94d,

            executionTier: WorkerExecutionTiers.Tier0,

            autoExecutionEligible: false));



        var projectBundle = GetProjectBundle(workspaceId, query, 6, 6);

        ProjectDeepScanReportResponse? deepScan = null;

        ArtifactSummaryResponse? deepScanArtifact = null;

        StandardsResolution? standards = null;

        MemoryScopedSearchResponse? scopedMemory = null;



        if (projectBundle.Exists)

        {

            toolCards.Add(BuildToolCard(

                ToolNames.ProjectGetContextBundle,

                FirstNonEmpty(projectBundle.StatusCode, StatusCodes.ReadSucceeded),

                true,

                FirstNonEmpty(projectBundle.Summary, "Da nap workspace context bundle."),

                projectBundle,

                stage: WorkerFlowStages.Scan,

                confidence: 0.92d,

                executionTier: WorkerExecutionTiers.Tier0,

                autoExecutionEligible: false));



            if (ShouldResolveStandardsEvidence(query, projectBundle))

            {

                standards = _copilotTasks.ResolveStandards(new StandardsResolutionRequest

                {

                    WorkspaceId = workspaceId,

                    StandardKind = ResolveStandardsKindForResearch(query),

                    Discipline = ResolveDiscipline(query, intent),

                    RequestedKeys = BuildStandardsRequestedKeys(query)

                });

                toolCards.Add(BuildToolCard(

                    ToolNames.StandardsResolve,

                    StatusCodes.ReadSucceeded,

                    true,

                    FirstNonEmpty(standards.Summary, "Da resolve standards lien quan den workspace hien tai."),

                    standards,

                    stage: WorkerFlowStages.Scan,

                    confidence: 0.83d,

                    executionTier: WorkerExecutionTiers.Tier0,

                    autoExecutionEligible: false));

            }



            scopedMemory = SearchScopedResearchMemory(query, contextSummary.DocumentKey, workspaceId, intent);

            if ((scopedMemory.Hits?.Count ?? 0) > 0)

            {

                toolCards.Add(BuildToolCard(

                    ToolNames.MemorySearchScoped,

                    StatusCodes.ReadSucceeded,

                    true,

                    scopedMemory.Summary,

                    scopedMemory,

                    stage: WorkerFlowStages.Scan,

                    confidence: 0.79d,

                    executionTier: WorkerExecutionTiers.Tier0,

                    autoExecutionEligible: false));

            }

        }



        if (projectBundle.Exists && IsDeepScanGroundedStatus(projectBundle.DeepScanStatus))

        {

            deepScan = _copilotTasks.GetProjectDeepScan(new ProjectDeepScanGetRequest

            {

                WorkspaceId = workspaceId

            });

            if (deepScan.Exists)

            {

                toolCards.Add(BuildToolCard(

                    ToolNames.ProjectGetDeepScan,

                    FirstNonEmpty(deepScan.StatusCode, StatusCodes.ReadSucceeded),

                    true,

                    FirstNonEmpty(deepScan.Summary, projectBundle.DeepScanSummary, "Da nap Project Brain deep scan report."),

                    deepScan,

                    new[] { deepScan.ReportPath, deepScan.SummaryReportPath },

                    stage: WorkerFlowStages.Scan,

                    confidence: 0.9d,

                    executionTier: WorkerExecutionTiers.Tier0,

                    autoExecutionEligible: false));

                AddDistinctIgnoreCase(artifactRefs, deepScan.ReportPath);

                AddDistinctIgnoreCase(artifactRefs, deepScan.SummaryReportPath);



                var summarizePath = FirstNonEmpty(deepScan.SummaryReportPath, deepScan.ReportPath);

                if (!string.IsNullOrWhiteSpace(summarizePath))

                {

                    deepScanArtifact = _copilotTasks.SummarizeArtifact(new ArtifactSummarizeRequest

                    {

                        ArtifactPath = summarizePath,

                        MaxChars = 1800,

                        MaxLines = 48

                    });

                    if (deepScanArtifact.Exists)

                    {

                        toolCards.Add(BuildToolCard(

                            ToolNames.ArtifactSummarize,

                            StatusCodes.ReadSucceeded,

                            true,

                            FirstNonEmpty(deepScanArtifact.Summary, "Da tom tat deep scan artifact."),

                            deepScanArtifact,

                            new[] { summarizePath },

                            stage: WorkerFlowStages.Scan,

                            confidence: 0.82d,

                            executionTier: WorkerExecutionTiers.Tier0,

                            autoExecutionEligible: false));

                        AddDistinctIgnoreCase(artifactRefs, summarizePath);

                    }

                }

            }

        }



        ApplyResearchGroundingMetadata(contextSummary, projectBundle, deepScan, deepScanArtifact, standards, scopedMemory);

        return BuildGroundedResearchResponseText(contextSummary, projectBundle, deepScan, standards, scopedMemory);

    }



    private void RunQc(UIApplication uiapp, Document doc, string message, ICollection<WorkerToolCard> toolCards, ICollection<string> artifactRefs, out string responseText)

    {

        var modelHealth = _platform.ReviewModelHealth(uiapp, doc);

        toolCards.Add(BuildToolCard(

            ToolNames.ReviewModelHealth,

            StatusCodes.ReadSucceeded,

            true,

            $"Model health: {modelHealth.TotalWarnings} warning(s), {modelHealth.LoadedLinks}/{modelHealth.TotalLinks} link(s) loaded.",

            modelHealth));



        var warnings = _platform.ReviewWarnings(doc);

        toolCards.Add(BuildToolCard(

            ToolNames.ReviewModelWarnings,

            StatusCodes.ReadSucceeded,

            true,

            $"Warnings review: {warnings.IssueCount} issue(s) captured.",

            warnings));



        var smartQc = _smartQc.Run(uiapp, doc, new SmartQcRequest

        {

            DocumentKey = _platform.GetDocumentKey(doc),

            RulesetName = ResolveRulesetName(message)

        });

        toolCards.Add(BuildToolCard(ToolNames.ReviewSmartQc, StatusCodes.ReadSucceeded, true, smartQc.Summary, smartQc));



        foreach (var finding in smartQc.Findings.Take(5))

        {

            if (!string.IsNullOrWhiteSpace(finding.EvidenceRef))

            {

                artifactRefs.Add(finding.EvidenceRef);

            }

        }



        responseText = $"Em da QC model read-only. {smartQc.Summary} Model health dang ghi nhan {modelHealth.TotalWarnings} warning(s).";

    }



    private void RunSheetAnalysis(UIApplication uiapp, Document doc, string message, ICollection<WorkerToolCard> toolCards, ICollection<string> artifactRefs, out string responseText)

    {

        var request = BuildSheetRequest(doc, uiapp, message);

        var capture = _sheetIntelligence.Capture(_platform, doc, request);

        toolCards.Add(BuildToolCard(

            ToolNames.SheetCaptureIntelligence,

            StatusCodes.ReadSucceeded,

            true,

            capture.Summary,

            capture,

            capture.Artifacts.Select(x => x.Path)));



        foreach (var artifact in capture.Artifacts)

        {

            if (!string.IsNullOrWhiteSpace(artifact.Path))

            {

                artifactRefs.Add(artifact.Path);

            }

        }



        var smartQc = _smartQc.Run(uiapp, doc, new SmartQcRequest

        {

            DocumentKey = _platform.GetDocumentKey(doc),

            RulesetName = ResolveRulesetName(message),

            SheetIds = request.SheetId > 0 ? new List<int> { request.SheetId } : new List<int>(),

            SheetNumbers = string.IsNullOrWhiteSpace(request.SheetNumber) ? new List<string>() : new List<string> { request.SheetNumber },

            MaxSheets = 1

        });

        toolCards.Add(BuildToolCard(ToolNames.ReviewSmartQc, StatusCodes.ReadSucceeded, true, smartQc.Summary, smartQc));



        if (message.IndexOf("schedule", StringComparison.OrdinalIgnoreCase) >= 0 && capture.Schedules.Count > 0)

        {

            var firstSchedule = capture.Schedules[0];

            var scheduleData = _scheduleExtraction.Extract(_platform, doc, new ScheduleExtractionRequest

            {

                DocumentKey = _platform.GetDocumentKey(doc),

                ScheduleId = firstSchedule.ScheduleViewId,

                MaxRows = 50,

                IncludeColumnMetadata = true

            });

            toolCards.Add(BuildToolCard(ToolNames.DataExtractScheduleStructured, StatusCodes.ReadSucceeded, true, scheduleData.Summary, scheduleData));

        }



        responseText = $"Em da phan tich sheet {capture.SheetNumber} / {capture.SheetName}. {capture.Summary} QC summary: {smartQc.Summary}";

    }



    private void RunFamilyAnalysis(UIApplication uiapp, Document doc, string message, ICollection<WorkerToolCard> toolCards, out string responseText)

    {

        var request = BuildFamilyRequest(uiapp, doc, message);

        var xray = _familyXray.Xray(_platform, doc, request);

        toolCards.Add(BuildToolCard(ToolNames.FamilyXray, StatusCodes.ReadSucceeded, true, xray.Summary, xray));

        responseText = $"Em da phan tich family {xray.FamilyName}. {xray.Summary}";

    }



    private string HandleSheetAuthoringIntent(

        UIApplication uiapp,

        Document doc,

        ToolRequestEnvelope envelope,

        string message,

        WorkerConversationSessionState session,

        PlaybookRecommendation selectedPlaybook,

        PlaybookPreviewResponse playbookPreview,

        ICollection<WorkerActionCard> actionCards,

        ICollection<WorkerToolCard> toolCards)

    {

        if (string.IsNullOrWhiteSpace(selectedPlaybook?.PlaybookId))

        {

            _missions.Block(session, "Chua resolve duoc playbook sheet phu hop.", "Can them standards/playbook pack hoac user clarification.");

            actionCards.Add(new WorkerActionCard

            {

                ActionKind = WorkerActionKinds.Clarify,

                Title = "Bo sung scope sheet",

                Summary = "Noi ro discipline, levels, sheet name, title block, hoac team standard muon dung.",

                ToolName = ToolNames.PlaybookMatch,

                IsPrimary = true,

                ExecutionTier = WorkerExecutionTiers.Tier0,

                WhyThisAction = "Sheet authoring phai resolve duoc standards + playbook truoc khi chain tool.",

                Confidence = 0.82d,

                RecoveryHint = "Neu standards pack chua co, them standards vao packs/standards va workspace.json.",

                AutoExecutionEligible = false

            });



            return "Em chua resolve duoc playbook sheet phu hop cho workspace hien tai, nen se khong doan tool chain.";

        }

        var resolvedPlaybook = selectedPlaybook ?? new PlaybookRecommendation();

        if (_playbookExecution.TryPrepare(uiapp, doc, envelope, session, message, resolvedPlaybook, playbookPreview, out var prepared) && prepared.Handled)

        {

            foreach (var card in prepared.ToolCards)

            {

                toolCards.Add(card);

            }



            foreach (var card in prepared.ActionCards)

            {

                actionCards.Add(card);

            }



            if (prepared.AwaitingApproval)

            {

                _conversations.SetPendingApproval(session.SessionId, prepared.PendingApproval);

                _missions.AwaitApproval(session, prepared.PendingApproval.Summary, new[] { resolvedPlaybook.PlaybookId });

            }

            else

            {

                _missions.Block(session, prepared.ResponseText, "Playbook preview bi block o standards/level preflight.");

            }



            return prepared.ResponseText;

        }



        toolCards.Add(BuildToolCard(

            ToolNames.PlaybookPreview,

            StatusCodes.ReadSucceeded,

            true,

            playbookPreview.Summary,

            playbookPreview,

            stage: WorkerFlowStages.Preview,

            progress: 100,

            whyThisTool: "Worker front door uu tien playbook + standards + preview tool chain truoc khi authoring.",

            confidence: Math.Max(0.78d, resolvedPlaybook.Confidence),

            recoveryHints: new[]

            {

                "Neu standards key con thieu, cap nhat pack standards truoc khi execute.",

                "Neu playbook dung nhung inputs chua du, bo sung level/sheet name/title block."

            },

            executionTier: WorkerExecutionTiers.Tier0,

            autoExecutionEligible: false));



        _missions.Complete(session, "Da resolve playbook/standards cho sheet authoring va preview chain.");

        return $"Em da resolve playbook {resolvedPlaybook.PlaybookId}. {playbookPreview.Summary}";

    }



    private string HandleQuickPathIntent(

        UIApplication uiapp,

        Document doc,

        ToolRequestEnvelope envelope,

        string message,

        WorkerDecision decision,

        WorkerConversationSessionState session,

        ICollection<WorkerToolCard> toolCards,

        ICollection<WorkerActionCard> actionCards,

        ICollection<string> artifactRefs)

    {

        var workspaceId = ResolveWorkspaceId(doc);

        var quickSearchQuery = string.IsNullOrWhiteSpace(decision.PreferredCommandId) ? message : decision.PreferredCommandId;

        var quickRequest = BuildQuickActionRequest(uiapp, doc, quickSearchQuery, workspaceId, decision.Intent);

        var quickPlan = _commandAtlas.PlanQuickAction(GetToolCatalog(), quickRequest);

        var matchedEntry = quickPlan.MatchedEntry ?? new CommandAtlasEntry();

        toolCards.Add(BuildToolCard(

            ToolNames.WorkflowQuickPlan,

            StatusCodes.ReadSucceeded,

            true,

            quickPlan.Summary,

            quickPlan,

            stage: WorkerFlowStages.Plan,

            progress: 100,

            whyThisTool: "Quick-path dung command atlas + context auto-fill truoc khi vao playbook chain nang hon.",

            confidence: quickPlan.Confidence,

            recoveryHints: new[]

            {

                "Neu quick-path miss scope, bo sung active view/level/sheet hoac ten template.",

                "Neu command mapped-only, fallback sang playbook hoac command atlas coverage report."

            },

            executionTier: WorkerExecutionTiers.Tier0,

            autoExecutionEligible: false));



        if (string.IsNullOrWhiteSpace(matchedEntry.CommandId))

        {

            _missions.Block(session, "Quick-path khong resolve duoc command phu hop.", quickPlan.Summary);

            actionCards.Add(CreateClarifyAction("Noi ro quick action", quickPlan.Summary));

            AppendFallbackActionCard(actionCards, quickPlan.FallbackProposal, true);

            return string.IsNullOrWhiteSpace(quickPlan.FallbackProposal.Summary)

                ? "Em chua resolve duoc quick action phu hop trong command atlas. Anh mo ta ro hon command/scope can lam."

                : quickPlan.FallbackProposal.Summary;

        }



        if (quickPlan.RequiresClarification)

        {

            _missions.Block(session, "Quick-path can them context truoc khi dispatch.", quickPlan.Summary);

            actionCards.Add(CreateClarifyAction(

                "Bo sung context",

                $"Quick action `{matchedEntry.DisplayName}` can them: {string.Join(", ", quickPlan.MissingContext)}."));

            return $"Quick action `{matchedEntry.DisplayName}` da duoc resolve, nhung can them context: {string.Join(", ", quickPlan.MissingContext)}.";

        }



        if (string.Equals(quickPlan.ExecutionDisposition, "mapped_only", StringComparison.OrdinalIgnoreCase))

        {

            _missions.Block(session, "Command da mapped nhung chua co lane execute.", quickPlan.Summary);

            AppendFallbackActionCard(actionCards, quickPlan.FallbackProposal, true);

            actionCards.Add(new WorkerActionCard

            {

                ActionKind = WorkerActionKinds.Suggest,

                Title = "Xem coverage report",

                Summary = "Command nay da duoc map trong atlas nhung chua co execution lane.",

                ToolName = ToolNames.CommandCoverageReport,

                IsPrimary = true,

                ExecutionTier = WorkerExecutionTiers.Tier0,

                WhyThisAction = "Coverage report cho biet command da mapped/executable/previewable/verified toi dau.",

                Confidence = 0.9d,

                RecoveryHint = "Neu can command nay, bo sung wrapper tool hoac curated script pack.",

                AutoExecutionEligible = false

            });

            return $"{quickPlan.Summary} {quickPlan.FallbackProposal.PreviewSummary}".Trim();

        }



        if (!AgentHost.TryGetCurrent(out var runtime) || runtime == null || !runtime.Registry.TryGet(ToolNames.CommandExecuteSafe, out var commandExecute))

        {

            _missions.Block(session, "Command execute lane khong san sang.", "Tool registry chua register command.execute_safe.");

            AppendFallbackActionCard(actionCards, quickPlan.FallbackProposal);

            return "Quick-path da resolve command, nhung execution lane command.execute_safe chua san sang.";

        }



        var allowAutoExecute = string.Equals(quickPlan.ExecutionDisposition, "direct", StringComparison.OrdinalIgnoreCase)

                               && matchedEntry.CanAutoExecute

                               && !matchedEntry.NeedsApproval

                               && !matchedEntry.CanPreview;

        var commandRequest = new CommandExecuteRequest

        {

            WorkspaceId = workspaceId,

            CommandId = matchedEntry.CommandId,

            Query = message,

            PayloadJson = quickPlan.ResolvedPayloadJson,

            AllowAutoExecute = allowAutoExecute,

            TargetDocument = envelope.TargetDocument,

            TargetView = envelope.TargetView

        };

        var executeEnvelope = BuildWorkerToolEnvelope(

            session,

            envelope,

            ToolNames.CommandExecuteSafe,

            JsonUtil.Serialize(commandRequest),

            !allowAutoExecute,

            string.Empty,

            string.Empty,

            string.Empty);

        var executed = commandExecute.Handler(uiapp, executeEnvelope);

        var executionPayload = string.IsNullOrWhiteSpace(executed.PayloadJson)

            ? new CommandExecuteResponse

            {

                StatusCode = executed.StatusCode,

                Summary = quickPlan.Summary,

                Entry = matchedEntry,

                ToolName = quickPlan.PlannedToolName,

                ConfirmationRequired = executed.ConfirmationRequired,

                ApprovalToken = executed.ApprovalToken,

                PreviewRunId = executed.PreviewRunId

            }

            : JsonUtil.DeserializeRequired<CommandExecuteResponse>(executed.PayloadJson);



        toolCards.Add(BuildToolCard(

            ToolNames.CommandExecuteSafe,

            executed.StatusCode,

            executed.Succeeded || executed.ConfirmationRequired,

            executionPayload.Summary,

            executionPayload,

            executed.Artifacts,

            stage: executed.ConfirmationRequired ? WorkerFlowStages.Preview : (executed.Succeeded ? WorkerFlowStages.Done : WorkerFlowStages.Error),

            progress: executed.ConfirmationRequired ? 60 : 100,

            whyThisTool: "command.execute_safe giu quick-path nho gon nhung van ton trong preview/approval/verify cua underlying lane.",

            confidence: Math.Max(0.7d, quickPlan.Confidence),

            recoveryHints: new[]

            {

                "Neu payload/context sai, dung command.describe hoac workflow.quick_plan de xem lai required context.",

                "Neu tool fail-closed, fallback sang playbook lan co policy/specialist ro hon."

            },

            executionTier: executed.ConfirmationRequired ? WorkerExecutionTiers.Tier1 : InferExecutionTier(executionPayload.ToolName),

            autoExecutionEligible: allowAutoExecute));



        foreach (var artifact in executed.Artifacts ?? new List<string>())

        {

            artifactRefs.Add(artifact);

        }



        if (executed.ConfirmationRequired)

        {

            _conversations.SetPendingApproval(session.SessionId, new WorkerPendingApprovalState

            {

                PendingActionId = Guid.NewGuid().ToString("N"),

                ToolName = ToolNames.CommandExecuteSafe,

                PayloadJson = JsonUtil.Serialize(commandRequest),

                ApprovalToken = executed.ApprovalToken,

                PreviewRunId = executed.PreviewRunId,

                ExpectedContextJson = executeEnvelope.ExpectedContextJson ?? string.Empty,

                Summary = executionPayload.Summary,

                ExpiresUtc = _clock.UtcNow.AddMinutes(_platform.Settings.ApprovalTokenTtlMinutes),

                ExecutionTier = WorkerExecutionTiers.Tier1,

                AutoExecutionEligible = false

            });

            _missions.AwaitApproval(session, executionPayload.Summary, new[] { matchedEntry.CommandId, executionPayload.ToolName });

            actionCards.Add(new WorkerActionCard

            {

                ActionKind = WorkerActionKinds.Approve,

                Title = "Dong y quick action",

                Summary = "Go 'dong y' de execute dung quick-path preview nay.",

                ToolName = ToolNames.CommandExecuteSafe,

                RequiresApproval = true,

                IsPrimary = true,

                ExecutionTier = WorkerExecutionTiers.Tier1,

                WhyThisAction = "Quick-path mutation van bi gate boi approval token va expected context.",

                Confidence = Math.Max(0.78d, quickPlan.Confidence),

                RecoveryHint = "Neu token drift, worker se yeu cau quick-plan/preview lai.",

                AutoExecutionEligible = false

            });

            return executionPayload.Summary;

        }



        if (executed.Succeeded)

        {

            _missions.Complete(session, executionPayload.Summary);

        }

        else

        {

            _missions.Block(session, executionPayload.Summary, executed.StatusCode);

            AppendFallbackActionCard(

                actionCards,

                HasProposal(executionPayload.FallbackProposal) ? executionPayload.FallbackProposal : quickPlan.FallbackProposal);

            actionCards.Add(CreateClarifyAction("Quick-path fallback", "Quick-path da bi block; bo sung scope hoac chuyen sang playbook lane."));

        }



        return executionPayload.Summary;

    }



    private string HandleMutationIntent(UIApplication uiapp, Document doc, ToolRequestEnvelope envelope, string message, WorkerConversationSessionState session, ICollection<WorkerToolCard> toolCards, ICollection<WorkerActionCard> actionCards)

    {

        _missions.Block(session, "MVP mutation surface dang gioi han trong curated sheet/view commands.", "Mutation ngoai curated 10-command surface se khong duoc worker route tu dong.");

        actionCards.Add(CreateClarifyAction(

            "Noi ro mutation MVP",

            "MVP hien uu tien create/duplicate/place/renumber view-sheet va read-only QC/export schedule."));

        return "MVP worker hien chi route mutation trong curated sheet/view surface. Neu can, anh noi ro lenh create/duplicate/place/renumber hoac chuyen sang command palette.";

    }



    private string ApprovePending(UIApplication uiapp, Document doc, ToolRequestEnvelope envelope, WorkerConversationSessionState session, ICollection<WorkerToolCard> toolCards, ICollection<string> artifactRefs, ICollection<WorkerActionCard> actionCards)

    {

        if (!session.PendingApprovalState.HasPendingApproval)

        {

            _missions.Block(session, "Khong co approval pending de execute.", "Worker approval path chi chay khi preview hop le con song.");

            return "Hien khong co preview pending de anh approve.";

        }



        var pending = session.PendingApprovalState;

        if (_playbookExecution.TryExecutePending(uiapp, doc, envelope, session, pending, out var playbookExecution) && playbookExecution.Handled)

        {

            foreach (var card in playbookExecution.ToolCards)

            {

                toolCards.Add(card);

            }



            foreach (var artifact in playbookExecution.ArtifactRefs)

            {

                artifactRefs.Add(artifact);

            }



            foreach (var card in playbookExecution.ActionCards)

            {

                actionCards.Add(card);

            }



            if (playbookExecution.ConsumesPendingApproval)

            {

                _conversations.ClearPendingApproval(session.SessionId);

            }



            if (playbookExecution.Succeeded)

            {

                _missions.MarkVerifying(session, "Da execute playbook package.");

                _missions.Complete(session, "Playbook package execution hoan tat.");

            }

            else

            {

                _missions.Fail(session, "Playbook package execution co residual hoac fail.", playbookExecution.ResponseText);

            }



            CaptureSessionMemory(session, WorkerMemoryKinds.ApprovalDecision, playbookExecution.ResponseText, doc, pending.ToolName, "approval", "playbook_execute");

            return playbookExecution.ResponseText;

        }



        if (string.Equals(pending.ToolName, ToolNames.AuditPurgeUnusedSafe, StringComparison.OrdinalIgnoreCase))

        {

            var payload = JsonUtil.DeserializeRequired<PurgeUnusedRequest>(pending.PayloadJson);

            var executeEnvelope = BuildWorkerToolEnvelope(session, envelope, pending.ToolName, pending.PayloadJson, false, pending.ApprovalToken, pending.PreviewRunId, pending.ExpectedContextJson);

            var approvalStatus = _platform.ValidateApprovalRequest(uiapp, doc, executeEnvelope);

            if (!string.Equals(approvalStatus, StatusCodes.Ok, StringComparison.OrdinalIgnoreCase))

            {

                _missions.Fail(session, "Approval token khong con hop le.", approvalStatus);

                actionCards.Add(new WorkerActionCard

                {

                    ActionKind = WorkerActionKinds.Clarify,

                    Title = "Preview láº¡i",

                    Summary = "Approval token het han hoac lech context. Hay preview lai mutation.",

                    ToolName = ToolNames.AuditPurgeUnusedSafe,

                    IsPrimary = true,

                    ExecutionTier = WorkerExecutionTiers.Tier2,

                    WhyThisAction = "Preview lai de lam moi token va expected context truoc execute.",

                    Confidence = 0.94d,

                    RecoveryHint = "Doc delta/context truoc khi preview lai neu model da doi.",

                    AutoExecutionEligible = false

                });

                return $"Approval bi chan: {approvalStatus}. Anh can preview lai mutation nay.";

            }



            _missions.MarkRunning(session, "execute_purge_unused");

            var result = _audit.ExecutePurgeUnused(uiapp, _platform, doc, payload);

            toolCards.Add(BuildToolCard(

                ToolNames.AuditPurgeUnusedSafe,

                StatusCodes.ExecuteSucceeded,

                true,

                $"Da purge {result.ChangedIds.Count} item(s).",

                result,

                result.Artifacts,

                stage: WorkerStages.Verification,

                progress: 100,

                whyThisTool: "Execute chi dien ra sau khi token/context hop le.",

                confidence: 0.9d,

                recoveryHints: new[] { "Chay context.get_delta_summary de xem residual neu can.", "Neu can verify sau purge, xem session.get_recent_operations." },

                executionTier: WorkerExecutionTiers.Tier2,

                autoExecutionEligible: false));

            foreach (var artifact in result.Artifacts)

            {

                artifactRefs.Add(artifact);

            }



            _conversations.ClearPendingApproval(session.SessionId);

            _missions.MarkVerifying(session, "Da execute mutation theo approval token.");

            _missions.Complete(session, "Execute purge unused hoan tat.");

            CaptureSessionMemory(session, WorkerMemoryKinds.ApprovalDecision, "Approved and executed purge unused.", doc, ToolNames.AuditPurgeUnusedSafe, "approval", "execute");

            return $"Em da execute purge unused an toan. Tong cong {result.ChangedIds.Count} item(s) da duoc xu ly.";

        }



        if (string.Equals(pending.ToolName, ToolNames.CommandExecuteSafe, StringComparison.OrdinalIgnoreCase))

        {

            if (!AgentHost.TryGetCurrent(out var runtime) || runtime == null || !runtime.Registry.TryGet(ToolNames.CommandExecuteSafe, out var registration))

            {

                _missions.Fail(session, "command.execute_safe khong con san sang.", ToolNames.CommandExecuteSafe);

                return "command.execute_safe khong con san sang de complete approval pending.";

            }



            var executeEnvelope = BuildWorkerToolEnvelope(session, envelope, pending.ToolName, pending.PayloadJson, false, pending.ApprovalToken, pending.PreviewRunId, pending.ExpectedContextJson);

            var executed = registration.Handler(uiapp, executeEnvelope);

            var payload = string.IsNullOrWhiteSpace(executed.PayloadJson)

                ? new CommandExecuteResponse

                {

                    StatusCode = executed.StatusCode,

                    Summary = executed.StatusCode,

                    ToolName = pending.ToolName,

                    ConfirmationRequired = executed.ConfirmationRequired,

                    ApprovalToken = executed.ApprovalToken,

                    PreviewRunId = executed.PreviewRunId

                }

                : JsonUtil.DeserializeRequired<CommandExecuteResponse>(executed.PayloadJson);



            toolCards.Add(BuildToolCard(

                ToolNames.CommandExecuteSafe,

                executed.StatusCode,

                executed.Succeeded || executed.ConfirmationRequired,

                payload.Summary,

                payload,

                executed.Artifacts,

                stage: executed.ConfirmationRequired ? WorkerFlowStages.Preview : (executed.Succeeded ? WorkerFlowStages.Verify : WorkerFlowStages.Error),

                progress: executed.ConfirmationRequired ? 60 : 100,

                whyThisTool: "Approval nay tiep tuc dung command atlas quick-path va preserve underlying guard rails.",

                confidence: 0.86d,

                recoveryHints: new[]

                {

                    "Neu approval bi drift, quick-plan lai de refresh command context.",

                    "Neu underlying tool bi block, fallback sang playbook/domain lane."

                },

                executionTier: InferExecutionTier(payload.ToolName),

                autoExecutionEligible: false));



            foreach (var artifact in executed.Artifacts)

            {

                artifactRefs.Add(artifact);

            }



            if (executed.ConfirmationRequired)

            {

                _conversations.SetPendingApproval(session.SessionId, new WorkerPendingApprovalState

                {

                    PendingActionId = pending.PendingActionId,

                    ToolName = ToolNames.CommandExecuteSafe,

                    PayloadJson = pending.PayloadJson,

                    ApprovalToken = executed.ApprovalToken,

                    PreviewRunId = executed.PreviewRunId,

                    ExpectedContextJson = pending.ExpectedContextJson,

                    Summary = payload.Summary,

                    ExpiresUtc = _clock.UtcNow.AddMinutes(_platform.Settings.ApprovalTokenTtlMinutes),

                    ExecutionTier = InferExecutionTier(payload.ToolName),

                    AutoExecutionEligible = false

                });

                _missions.AwaitApproval(session, payload.Summary, new[] { ToolNames.CommandExecuteSafe });

                return payload.Summary;

            }



            _conversations.ClearPendingApproval(session.SessionId);

            if (executed.Succeeded)

            {

                _missions.MarkVerifying(session, "Quick action approval execution hoan tat.");

                _missions.Complete(session, payload.Summary);

                CaptureSessionMemory(session, WorkerMemoryKinds.ApprovalDecision, payload.Summary, doc, ToolNames.CommandExecuteSafe, "approval", "quick_execute");

            }

            else

            {

                _missions.Fail(session, payload.Summary, executed.StatusCode);

            }



            return payload.Summary;

        }



        _missions.Block(session, "Pending approval chua duoc worker ho tro execute.", pending.ToolName);

        return $"Worker hien tai chua ho tro execute pending tool {pending.ToolName} qua approval flow nay.";

    }



    private string RejectPending(WorkerConversationSessionState session)

    {

        if (!session.PendingApprovalState.HasPendingApproval)

        {

            _missions.Block(session, "Khong co preview pending de tu choi.");

            return "Hien khong co preview pending de tu choi.";

        }



        _conversations.ClearPendingApproval(session.SessionId);

        _missions.Block(session, "Operator da tu choi preview pending.", "Mission dung o checkpoint approval.");

        return "Da huy preview pending. Em khong execute gi them.";

    }



    private string CancelMission(WorkerConversationSessionState session)

    {

        _conversations.ClearPendingApproval(session.SessionId);

        _missions.Block(session, "Mission da duoc huy theo yeu cau.");

        return "Da huy mission hien tai. Khi can, anh noi nhiem vu moi hoac go 'tiep tuc'.";

    }



    private string ResumeMission(WorkerConversationSessionState session, ICollection<WorkerActionCard> actionCards)

    {

        if (session.PendingApprovalState.HasPendingApproval)

        {

            _missions.AwaitApproval(session, session.PendingApprovalState.Summary, session.Mission.PlannedTools);

            actionCards.Add(new WorkerActionCard

            {

                ActionKind = WorkerActionKinds.Approve,

                Title = "Dong y preview pending",

                Summary = "Preview truoc do van con pending approval.",

                ToolName = session.PendingApprovalState.ToolName,

                RequiresApproval = true,

                IsPrimary = true,

                ExecutionTier = InferExecutionTier(session.PendingApprovalState.ToolName),

                WhyThisAction = "Resume dua mission tro lai dung checkpoint approval cu.",

                Confidence = 0.87d,

                RecoveryHint = "Neu context da doi, worker van co the block va yeu cau preview lai.",

                AutoExecutionEligible = session.PendingApprovalState.AutoExecutionEligible

            });

            return "Mission dang co preview pending. Anh co the go 'dong y' hoac 'tu choi'.";

        }



        if (string.Equals(session.Mission.Status, WorkerMissionStates.Completed, StringComparison.OrdinalIgnoreCase))

        {

            return "Mission truoc da hoan tat. Anh hay giao nhiem vu moi de em tao mission moi.";

        }



        return "Em da restore mission gan nhat. Anh noi ro buoc tiep theo hoac dung suggestion card.";

    }



    private WorkerContextSummary BuildContextSummary(UIApplication uiapp, Document doc, string query, string missionType)

    {

        var taskContext = BuildTaskContext(uiapp, doc, 10, 10);

        var deltaSummary = BuildDeltaSummary(uiapp, doc, 10, 10);

        var documentKey = _platform.GetDocumentKey(doc);

        var similarEpisodeHints = _episodicMemory.Search(query, documentKey, missionType, 3)

            .Select(x => $"{x.MissionType}: {x.Outcome}")

            .ToList();



        var similarRuns = _copilotTasks.FindSimilarRuns(new MemoryFindSimilarRunsRequest

        {

            Query = query,

            DocumentKey = documentKey,

            TaskKind = missionType,

            MaxResults = 3

        });

        similarEpisodeHints.AddRange(similarRuns.Runs.Select(x => $"{x.TaskKind}:{x.TaskName} - {x.Status}"));



        var workspaceId = ResolveWorkspaceId(doc);

        var contextSummary = new WorkerContextSummary

        {

            DocumentKey = taskContext.Document.DocumentKey,

            DocumentTitle = taskContext.Document.Title,

            ActiveViewKey = doc.ActiveView != null ? $"view:{doc.ActiveView.Id.Value}" : string.Empty,

            ActiveViewName = taskContext.ActiveContext.ViewName,

            SelectionCount = taskContext.Selection.Count,

            Summary = deltaSummary.Summary,

            SuggestedNextTools = deltaSummary.SuggestedNextTools.ToList(),

            SimilarEpisodeHints = similarEpisodeHints

                .Where(x => !string.IsNullOrWhiteSpace(x))

                .Distinct(StringComparer.OrdinalIgnoreCase)

                .Take(5)

                .ToList(),

            WorkspaceId = workspaceId,

            GroundingLevel = WorkerGroundingLevels.LiveContextOnly,

            GroundingSummary = "Dang dua tren live Revit context.",

            GroundingRefs = new List<string>()

        };

        EnrichProjectContextSummary(contextSummary, workspaceId, query);

        return contextSummary;

    }



    private void EnrichProjectContextSummary(WorkerContextSummary summary, string workspaceId, string query)

    {

        if (summary == null || string.IsNullOrWhiteSpace(workspaceId))

        {

            return;

        }



        var projectBundle = _copilotTasks.GetProjectContextBundle(GetToolCatalog(), new ProjectContextBundleRequest

        {

            WorkspaceId = workspaceId,

            Query = query,

            MaxSourceRefs = 4,

            MaxStandardsRefs = 4

        });

        summary.ProjectSummary = projectBundle.Summary ?? string.Empty;

        summary.ProjectPrimaryModelStatus = projectBundle.PrimaryModelStatus ?? string.Empty;

        summary.ProjectTopRefs = (projectBundle.TopStandardsRefs ?? new List<ProjectContextRef>())

            .Concat(projectBundle.SourceRefs ?? new List<ProjectContextRef>())

            .Select(x => string.IsNullOrWhiteSpace(x.Title) ? x.RefId : $"{x.RefKind}:{x.Title}")

            .Where(x => !string.IsNullOrWhiteSpace(x))

            .Take(6)

            .ToList();

        summary.ProjectPendingUnknowns = projectBundle.PendingUnknowns?.ToList() ?? new List<string>();

        if (!projectBundle.Exists)

        {

            summary.GroundingLevel = WorkerGroundingLevels.LiveContextOnly;

            summary.GroundingSummary = "Dang dung live Revit context; project context chua init.";

            summary.GroundingRefs = new List<string>();

            return;

        }



        summary.GroundingLevel = IsDeepScanGroundedStatus(projectBundle.DeepScanStatus)

            ? WorkerGroundingLevels.DeepScanGrounded

            : WorkerGroundingLevels.WorkspaceGrounded;

        summary.GroundingSummary = IsDeepScanGroundedStatus(projectBundle.DeepScanStatus)

            ? FirstNonEmpty(projectBundle.DeepScanSummary, projectBundle.Summary, "Grounded by workspace + deep scan.")

            : FirstNonEmpty(projectBundle.Summary, "Grounded by workspace context bundle.");

        summary.GroundingRefs = (projectBundle.TopStandardsRefs ?? new List<ProjectContextRef>())

            .Concat(projectBundle.SourceRefs ?? new List<ProjectContextRef>())

            .Concat(projectBundle.DeepScanRefs ?? new List<ProjectContextRef>())

            .Select(ResolveRefLabel)

            .Where(x => !string.IsNullOrWhiteSpace(x))

            .Take(8)

            .ToList();

    }



    private TaskContextResponse BuildTaskContext(UIApplication uiapp, Document doc, int maxRecentOperations, int maxRecentEvents)

    {

        return _platform.GetTaskContext(uiapp, doc, new TaskContextRequest

        {

            DocumentKey = _platform.GetDocumentKey(doc),

            IncludeCapabilities = false,

            IncludeToolCatalog = false,

            MaxRecentOperations = maxRecentOperations,

            MaxRecentEvents = maxRecentEvents

        }, GetToolCatalog());

    }



    private ContextDeltaSummaryResponse BuildDeltaSummary(UIApplication uiapp, Document doc, int maxRecentOperations, int maxRecentEvents)

    {

        return _copilotTasks.GetContextDeltaSummary(

            uiapp,

            doc,

            new ContextDeltaSummaryRequest

            {

                DocumentKey = _platform.GetDocumentKey(doc),

                MaxRecentOperations = maxRecentOperations,

                MaxRecentEvents = maxRecentEvents

            },

            GetToolCatalog(),

            GetQueueState());

    }



    private Document ResolveDocument(UIApplication uiapp, ToolRequestEnvelope envelope)

    {

        return _platform.ResolveDocument(uiapp, envelope.TargetDocument);

    }



    private string ResolveWorkspaceId(Document doc)

    {

        return _copilotTasks.ResolveWorkspaceIdForDocument(doc);

    }



    private static string ResolveDocumentContext(Document doc, string intent)

    {

        if (doc != null && doc.IsFamilyDocument)

        {

            return "family_document";

        }



        if (string.Equals(intent, "sheet_analysis_request", StringComparison.OrdinalIgnoreCase))

        {

            return "project";

        }



        return "project";

    }



    private QuickActionRequest BuildQuickActionRequest(UIApplication uiapp, Document doc, string message, string workspaceId, string intent)

    {

        var request = new QuickActionRequest

        {

            WorkspaceId = workspaceId,

            Query = message ?? string.Empty,

            Discipline = ResolveDiscipline(message ?? string.Empty, intent),

            DocumentContext = ResolveDocumentContext(doc, intent)

        };



        if (doc?.ActiveView != null)

        {

            request.ActiveViewId = checked((int)doc.ActiveView.Id.Value);

            request.ActiveViewName = doc.ActiveView.Name ?? string.Empty;

            request.ActiveViewType = doc.ActiveView.ViewType.ToString();



            if (doc.ActiveView is ViewPlan viewPlan && viewPlan.GenLevel != null)

            {

                request.CurrentLevelId = checked((int)viewPlan.GenLevel.Id.Value);

                request.CurrentLevelName = viewPlan.GenLevel.Name ?? string.Empty;

            }



            if (doc.ActiveView is ViewSheet sheet)

            {

                request.CurrentSheetId = checked((int)sheet.Id.Value);

                request.CurrentSheetNumber = sheet.SheetNumber ?? string.Empty;

            }

        }



        var uiDoc = uiapp?.ActiveUIDocument;

        if (uiDoc != null && doc != null && uiDoc.Document.Equals(doc))

        {

            request.SelectionCount = uiDoc.Selection.GetElementIds().Count;

        }



        return request;

    }



    private static WorkerActionCard CreateClarifyAction(string title, string summary)

    {

        return new WorkerActionCard

        {

            ActionKind = WorkerActionKinds.Clarify,

            Title = title,

            Summary = summary,

            IsPrimary = true,

            ExecutionTier = WorkerExecutionTiers.Tier0,

            WhyThisAction = "Worker can them context nho gon truoc khi route vao tool hoac playbook lane.",

            Confidence = 0.82d,

            RecoveryHint = "Bo sung scope, level, sheet, template, selection, hoac ten command cu the.",

            AutoExecutionEligible = false

        };

    }



    private static void AppendApprovalCheckpointActions(ICollection<WorkerActionCard> actionCards, WorkerPendingApprovalState? pendingApproval)

    {

        if (actionCards == null || pendingApproval == null || !pendingApproval.HasPendingApproval)

        {

            return;

        }



        var toolName = string.IsNullOrWhiteSpace(pendingApproval.ToolName) ? "pending_approval" : pendingApproval.ToolName;

        var summary = string.IsNullOrWhiteSpace(pendingApproval.Summary)

            ? "Đang có approval pending cần xử lý trước khi tiếp tục."

            : pendingApproval.Summary;



        actionCards.Add(new WorkerActionCard

        {

            ActionKind = WorkerActionKinds.Approve,

            Title = "Dong y preview pending",

            Summary = summary,

            ToolName = toolName,

            RequiresApproval = true,

            IsPrimary = true,

            ExecutionTier = ResolvePendingExecutionTier(pendingApproval),

            WhyThisAction = "Approval checkpoint phai duoc giai quyet truoc khi worker nhan task moi.",

            Confidence = 0.95d,

            RecoveryHint = "Neu context da doi, preview lai truoc khi approve.",

            AutoExecutionEligible = pendingApproval.AutoExecutionEligible

        });



        actionCards.Add(new WorkerActionCard

        {

            ActionKind = WorkerActionKinds.Reject,

            Title = "Tu choi preview pending",

            Summary = summary,

            ToolName = toolName,

            ExecutionTier = ResolvePendingExecutionTier(pendingApproval),

            WhyThisAction = "Tu choi la cach an toan nhat de giai phong checkpoint hien tai.",

            Confidence = 0.98d,

            RecoveryHint = "Sau khi tu choi, co the preview lai voi scope/context moi.",

            AutoExecutionEligible = false

        });



        actionCards.Add(new WorkerActionCard

        {

            ActionKind = WorkerActionKinds.Resume,

            Title = "Resume checkpoint",

            Summary = summary,

            ToolName = toolName,

            ExecutionTier = ResolvePendingExecutionTier(pendingApproval),

            WhyThisAction = "Resume se dua session ve dung checkpoint approval cu.",

            Confidence = 0.87d,

            RecoveryHint = "Neu context drift, worker van co the block va preview lai.",

            AutoExecutionEligible = pendingApproval.AutoExecutionEligible

        });

    }



    private static string BuildPendingApprovalCheckpointResponse(WorkerPendingApprovalState? pendingApproval, string requestedIntent)

    {

        var toolName = pendingApproval == null || string.IsNullOrWhiteSpace(pendingApproval.ToolName) ? "preview pending" : pendingApproval.ToolName;

        var summary = pendingApproval == null || string.IsNullOrWhiteSpace(pendingApproval.Summary)

            ? "Đang có approval pending."

            : pendingApproval.Summary;



        if (string.IsNullOrWhiteSpace(requestedIntent))

        {

            return $"Đang có approval pending cho `{toolName}`. {summary} Anh vui lòng approve, reject hoặc resume checkpoint này trước khi giao task mới.";

        }



        return $"Đang có approval pending cho `{toolName}` nên em tạm dừng intent `{requestedIntent}` để tránh context drift. {summary} Anh vui lòng approve, reject hoặc resume checkpoint này trước khi giao task mới.";

    }



    private static bool IsApprovalCheckpointIntent(string intent)

    {

        return string.Equals(intent, "approval", StringComparison.OrdinalIgnoreCase)

            || string.Equals(intent, "reject", StringComparison.OrdinalIgnoreCase)

            || string.Equals(intent, "cancel", StringComparison.OrdinalIgnoreCase)

            || string.Equals(intent, "resume", StringComparison.OrdinalIgnoreCase)

            || string.Equals(intent, "context_query", StringComparison.OrdinalIgnoreCase)

            || string.Equals(intent, "help", StringComparison.OrdinalIgnoreCase)

            || string.Equals(intent, "greeting", StringComparison.OrdinalIgnoreCase);

    }



    private static string ResolveCapabilityDomain(string intent, PlaybookRecommendation recommendation, PlaybookPreviewResponse preview)

    {

        if (!string.IsNullOrWhiteSpace(preview?.CapabilityDomain))

        {

            return preview!.CapabilityDomain;

        }



        if (!string.IsNullOrWhiteSpace(recommendation?.CapabilityDomain))

        {

            return recommendation!.CapabilityDomain;

        }



        return intent switch

        {

            "governance_request" => CapabilityDomains.Governance,

            "view_authoring_request" => CapabilityDomains.Governance,

            "documentation_request" => CapabilityDomains.Governance,

            "model_manage_request" => CapabilityDomains.Governance,

            "command_palette_request" => CapabilityDomains.Intent,

            "annotation_request" => CapabilityDomains.Annotation,

            "coordination_request" => CapabilityDomains.Coordination,

            "systems_request" => CapabilityDomains.Systems,

            "integration_request" => CapabilityDomains.Integration,

            "intent_compile_request" => CapabilityDomains.Intent,

            "family_analysis_request" => CapabilityDomains.FamilyQa,

            "sheet_authoring_request" => CapabilityDomains.Governance,

            "sheet_analysis_request" => CapabilityDomains.Governance,

            "element_authoring_request" => CapabilityDomains.General,

            _ => CapabilityDomains.General

        };

    }



    private static string ResolveDiscipline(string message, string intent)

    {

        var normalized = (message ?? string.Empty).ToLowerInvariant();

        if (normalized.Contains("duct") || normalized.Contains("ahu") || normalized.Contains("hvac"))

        {

            return CapabilityDisciplines.Mechanical;

        }



        if (normalized.Contains("pipe") || normalized.Contains("sanitary") || normalized.Contains("drain") || normalized.Contains("plumbing"))

        {

            return CapabilityDisciplines.Plumbing;

        }



        if (normalized.Contains("electrical") || normalized.Contains("tray") || normalized.Contains("wire") || normalized.Contains("panel"))

        {

            return CapabilityDisciplines.Electrical;

        }



        if (normalized.Contains("beam") || normalized.Contains("column") || normalized.Contains("slab") || normalized.Contains("struct"))

        {

            return CapabilityDisciplines.Structure;

        }



        return intent switch

        {

            "systems_request" => CapabilityDisciplines.Mep,

            "coordination_request" => CapabilityDisciplines.Mep,

            "annotation_request" => CapabilityDisciplines.Architecture,

            "view_authoring_request" => CapabilityDisciplines.Architecture,

            "documentation_request" => CapabilityDisciplines.Architecture,

            "model_manage_request" => CapabilityDisciplines.Common,

            "governance_request" => CapabilityDisciplines.Common,

            _ => CapabilityDisciplines.Common

        };

    }



    private static List<string> ResolveIssueKinds(string message, string capabilityDomain, string intent)

    {

        var results = new List<string>();

        var normalized = (message ?? string.Empty).ToLowerInvariant();

        switch (capabilityDomain)

        {

            case CapabilityDomains.Governance:

                AddDistinct(results, CapabilityIssueKinds.WarningTriage);

                if (normalized.Contains("sheet") || normalized.Contains("view"))

                {

                    AddDistinct(results, CapabilityIssueKinds.SheetPackage);

                    AddDistinct(results, CapabilityIssueKinds.NamingConvention);

                }

                if (normalized.Contains("parameter") || normalized.Contains("excel") || normalized.Contains("spec"))

                {

                    AddDistinct(results, CapabilityIssueKinds.ParameterPopulation);

                }

                if (normalized.Contains("lod") || normalized.Contains("loi"))

                {

                    AddDistinct(results, CapabilityIssueKinds.LodLoiCompliance);

                }

                break;

            case CapabilityDomains.Annotation:

                AddDistinct(results, CapabilityIssueKinds.TagOverlap);

                AddDistinct(results, CapabilityIssueKinds.DimensionCollision);

                AddDistinct(results, CapabilityIssueKinds.RoomFinishGeneration);

                break;

            case CapabilityDomains.Coordination:

                AddDistinct(results, CapabilityIssueKinds.HardClash);

                AddDistinct(results, CapabilityIssueKinds.ClearanceSoftClash);

                break;

            case CapabilityDomains.Systems:

                AddDistinct(results, CapabilityIssueKinds.DisconnectedSystem);

                AddDistinct(results, CapabilityIssueKinds.SlopeContinuity);

                AddDistinct(results, CapabilityIssueKinds.BasicRouting);

                break;

            case CapabilityDomains.Integration:

                AddDistinct(results, CapabilityIssueKinds.ExternalSync);

                AddDistinct(results, CapabilityIssueKinds.ScanToBim);

                AddDistinct(results, CapabilityIssueKinds.LargeModelSplit);

                break;

            case CapabilityDomains.Intent:

                AddDistinct(results, CapabilityIssueKinds.IntentCompile);

                break;

            case CapabilityDomains.FamilyQa:

                AddDistinct(results, CapabilityIssueKinds.FamilyQa);

                break;

        }



        if (results.Count == 0 && string.Equals(intent, "mutation_request", StringComparison.OrdinalIgnoreCase))

        {

            AddDistinct(results, CapabilityIssueKinds.WarningTriage);

        }



        return results;

    }



    private static string HandleCapabilityIntent(

        string intent,

        CompiledTaskPlan compiledPlan,

        ICollection<WorkerActionCard> actionCards,

        ICollection<WorkerToolCard> toolCards)

    {

        compiledPlan ??= new CompiledTaskPlan();

        toolCards.Add(new WorkerToolCard

        {

            ToolName = ToolNames.IntentCompile,

            StatusCode = StatusCodes.ReadSucceeded,

            Succeeded = true,

            Summary = compiledPlan.Summary,

            PayloadJson = JsonUtil.Serialize(compiledPlan),

            ExecutionTier = WorkerExecutionTiers.Tier0,

            WhyThisTool = "Compiled capability plan de route dung playbook/policy/specialist/tool lanes.",

            Confidence = 0.84d,

            RecoveryHints = new List<string>

            {

                "Neu plan chua dung, refine query voi discipline, issue kind, workspace policy, hoac expected outcome."

            },

            AutoExecutionEligible = false

        });



        if (compiledPlan.VerifyTools.Count > 0)

        {

            actionCards.Add(new WorkerActionCard

            {

                ActionKind = WorkerActionKinds.Suggest,

                Title = "Verify lane",

                Summary = $"Verify tools: {string.Join(", ", compiledPlan.VerifyTools.Take(3))}",

                ToolName = compiledPlan.VerifyTools[0],

                ExecutionTier = WorkerExecutionTiers.Tier0,

                WhyThisAction = "Moi capability lane deu can verify/evidence, khong chi execute.",

                Confidence = 0.8d,

                RecoveryHint = "Neu verify tools thieu, resolve them policy pack hoac review tools.",

                AutoExecutionEligible = false

            });

        }



        var domain = string.IsNullOrWhiteSpace(compiledPlan.CapabilityDomain) ? CapabilityDomains.General : compiledPlan.CapabilityDomain;

        return intent switch

        {

            "governance_request" => $"Em da compile governance plan. Domain={domain}; playbook={compiledPlan.RecommendedPlaybook.PlaybookId}; policies={compiledPlan.PolicyResolution.ResolvedPackIds.Count}; fix tools={compiledPlan.FixTools.Count}.",

            "annotation_request" => $"Em da compile annotation plan. Domain={domain}; playbook={compiledPlan.RecommendedPlaybook.PlaybookId}; issue-scan={compiledPlan.IssueScanTools.Count}; verify={compiledPlan.VerifyTools.Count}.",

            "coordination_request" => $"Em da compile coordination plan cho clash/clearance/opening. Policies={compiledPlan.PolicyResolution.ResolvedPackIds.Count}; specialists={compiledPlan.RecommendedSpecialists.Count}; fix tools={compiledPlan.FixTools.Count}.",

            "systems_request" => $"Em da compile systems plan cho connectivity/slope/routing. Verify={compiledPlan.VerifyTools.Count}; fix tools={compiledPlan.FixTools.Count}; specialists={compiledPlan.RecommendedSpecialists.Count}.",

            "integration_request" => $"Em da compile integration plan va preview lane connector-safe. Policies={compiledPlan.PolicyResolution.ResolvedPackIds.Count}; candidate tools={compiledPlan.CandidateToolNames.Count}.",

            "intent_compile_request" => $"Em da compile natural-language request thanh typed plan. Domain={domain}; candidate tools={compiledPlan.CandidateToolNames.Count}; verify={compiledPlan.VerifyTools.Count}.",

            _ => $"Em da compile capability plan. {compiledPlan.Summary}"

        };

    }



    private static string AppendPlaybookSummary(string currentSummary, PlaybookRecommendation recommendation, PlaybookPreviewResponse preview)

    {

        if (string.IsNullOrWhiteSpace(recommendation?.PlaybookId))

        {

            return currentSummary ?? string.Empty;

        }



        var standardsValues = (preview?.Standards?.Values ?? new List<StandardsResolvedValue>())

            .Where(x => x != null)

            .ToList();

        var matchedStandards = standardsValues.Count(x => x != null && x.Matched);

        var playbookId = recommendation?.PlaybookId ?? string.Empty;

        var addition = $"Playbook={playbookId}; standards={matchedStandards}/{standardsValues.Count}.";

        return string.IsNullOrWhiteSpace(currentSummary)

            ? addition

            : currentSummary + " " + addition;

    }



    private static string AppendPlaybookReasoning(string currentReasoning, PlaybookRecommendation recommendation, PlaybookPreviewResponse preview)

    {

        if (string.IsNullOrWhiteSpace(recommendation?.PlaybookId))

        {

            return currentReasoning ?? string.Empty;

        }



        var playbookId = recommendation?.PlaybookId ?? string.Empty;

        var requiredInputs = recommendation?.RequiredInputs ?? new List<string>();

        var addition = $"Orchestrator match={playbookId}; workspace={preview?.WorkspaceId ?? string.Empty}; requiredInputs={string.Join(",", requiredInputs)}.";

        return string.IsNullOrWhiteSpace(currentReasoning)

            ? addition

            : currentReasoning + " " + addition;

    }



    private static string BuildPackSummary(PlaybookRecommendation recommendation, PlaybookPreviewResponse preview)

    {

        var packId = recommendation?.PackId ?? string.Empty;

        var specialists = recommendation?.RecommendedSpecialists != null && recommendation.RecommendedSpecialists.Count > 0

            ? string.Join(", ", recommendation.RecommendedSpecialists)

            : "orchestrator-only";

        if (string.IsNullOrWhiteSpace(packId))

        {

            return string.IsNullOrWhiteSpace(preview?.PlaybookId)

                ? "Workspace chua resolve pack/playbook cu the."

                : $"Playbook {preview?.PlaybookId ?? string.Empty} dang chay tren orchestrator lane ({specialists}).";

        }



        return $"Pack {packId}; specialists={specialists}.";

    }



    private static void AddDistinct(ICollection<string> values, string value)

    {

        if (!string.IsNullOrWhiteSpace(value) && !values.Contains(value, StringComparer.OrdinalIgnoreCase))

        {

            values.Add(value);

        }

    }



    private string ResolvePersonaId(string requestedPersonaId)

    {

        var preferred = !string.IsNullOrWhiteSpace(requestedPersonaId)

            ? requestedPersonaId

            : _platform.Settings.DefaultWorkerProfile?.PersonaId ?? WorkerPersonas.RevitWorker;

        return _personas.Resolve(preferred).PersonaId;

    }



    private SheetCaptureIntelligenceRequest BuildSheetRequest(Document doc, UIApplication uiapp, string message)

    {

        var request = new SheetCaptureIntelligenceRequest

        {

            DocumentKey = _platform.GetDocumentKey(doc),

            IncludeViewportDetails = true,

            IncludeScheduleData = message.IndexOf("schedule", StringComparison.OrdinalIgnoreCase) >= 0

        };



        var explicitHint = ExtractSheetHint(message);

        if (!string.IsNullOrWhiteSpace(explicitHint))

        {

            request.SheetNumber = explicitHint;

            return request;

        }



        if (uiapp.ActiveUIDocument?.Document?.Equals(doc) == true && doc.ActiveView is ViewSheet activeSheet)

        {

            request.SheetId = checked((int)activeSheet.Id.Value);

            request.SheetNumber = activeSheet.SheetNumber ?? string.Empty;

        }



        return request;

    }



    private FamilyXrayRequest BuildFamilyRequest(UIApplication uiapp, Document doc, string message)

    {

        var request = new FamilyXrayRequest

        {

            DocumentKey = _platform.GetDocumentKey(doc)

        };



        if (doc.IsFamilyDocument && doc.OwnerFamily != null)

        {

            request.FamilyId = checked((int)doc.OwnerFamily.Id.Value);

            request.FamilyName = doc.OwnerFamily.Name ?? string.Empty;

            return request;

        }



        var selectedId = uiapp.ActiveUIDocument?.Selection.GetElementIds().FirstOrDefault();

        if (selectedId != null && selectedId != ElementId.InvalidElementId)

        {

            var element = doc.GetElement(selectedId);

            if (element is FamilyInstance familyInstance && familyInstance.Symbol?.Family != null)

            {

                request.FamilyId = checked((int)familyInstance.Symbol.Family.Id.Value);

                request.FamilyName = familyInstance.Symbol.Family.Name ?? string.Empty;

                return request;

            }



            if (element is Family family)

            {

                request.FamilyId = checked((int)family.Id.Value);

                request.FamilyName = family.Name ?? string.Empty;

                return request;

            }

        }



        var hint = ExtractQuotedName(message);

        if (!string.IsNullOrWhiteSpace(hint))

        {

            request.FamilyName = hint;

        }



        return request;

    }



    private static string ExtractQuotedName(string message)

    {

        var match = Regex.Match(message ?? string.Empty, "\"(?<name>[^\"]+)\"");

        return match.Success ? match.Groups["name"].Value : string.Empty;

    }



    private static string ExtractSheetHint(string message)

    {

        var match = Regex.Match(message ?? string.Empty, "\\b([A-Za-z]{0,2}\\d{2,4})\\b");

        return match.Success ? match.Groups[1].Value : string.Empty;

    }



    private static string ResolveRulesetName(string message)

    {

        if (message.IndexOf("vn", StringComparison.OrdinalIgnoreCase) >= 0 || message.IndexOf("viet", StringComparison.OrdinalIgnoreCase) >= 0)

        {

            return "vn-general";

        }



        if (message.IndexOf("us", StringComparison.OrdinalIgnoreCase) >= 0 || message.IndexOf("my", StringComparison.OrdinalIgnoreCase) >= 0)

        {

            return "us-residential";

        }



        return "base-rules";

    }



    private void PersistEpisode(WorkerConversationSessionState session, IEnumerable<WorkerToolCard> toolCards, IEnumerable<string> artifactRefs, string responseText)

    {

        var record = new EpisodicRecord

        {

            EpisodeId = Guid.NewGuid().ToString("N"),

            RunId = session.Mission.MissionId,

            MissionType = session.Mission.Intent,

            Outcome = responseText,

            KeyObservations = toolCards.Select(x => x.Summary).Where(x => !string.IsNullOrWhiteSpace(x)).Take(5).ToList(),

            KeyDecisions = new List<string>

            {

                session.Mission.PlanSummary,

                session.Mission.DecisionRationale

            }.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),

            ToolSequence = toolCards.Select(x => x.ToolName).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),

            ArtifactRefs = artifactRefs.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),

            DocumentKey = session.DocumentKey,

            CreatedUtc = _clock.UtcNow

        };



        _episodicMemory.Save(record);

        CaptureSessionMemory(session, WorkerMemoryKinds.MissionSummary, responseText, null, string.Empty, "episode", session.Mission.Intent);

    }



    private static bool ShouldPersistEpisode(WorkerConversationSessionState session)

    {

        if (session == null)

        {

            return false;

        }



        if (!string.Equals(session.Status, WorkerSessionStates.Active, StringComparison.OrdinalIgnoreCase))

        {

            return false;

        }



        if (!string.Equals(session.Mission.Status, WorkerMissionStates.Completed, StringComparison.OrdinalIgnoreCase)

            && !string.Equals(session.Mission.Status, WorkerMissionStates.Failed, StringComparison.OrdinalIgnoreCase)

            && !string.Equals(session.Mission.Status, WorkerMissionStates.Blocked, StringComparison.OrdinalIgnoreCase))

        {

            return false;

        }



        return !string.IsNullOrWhiteSpace(session.Mission.Intent)

               && !string.Equals(session.Mission.Intent, "greeting", StringComparison.OrdinalIgnoreCase)

               && !string.Equals(session.Mission.Intent, "help", StringComparison.OrdinalIgnoreCase);

    }



    private void CaptureSessionMemory(WorkerConversationSessionState session, string kind, string content, Document? doc, string toolName, params string[] tags)

    {

        if (session == null || string.IsNullOrWhiteSpace(content))

        {

            return;

        }



        var viewKey = string.Empty;

        if (doc != null && doc.ActiveView != null)

        {

            viewKey = $"view:{doc.ActiveView.Id.Value}";

        }



        _sessionMemory.Add(session.SessionId, new SessionMemoryEntry

        {

            EntryId = Guid.NewGuid().ToString("N"),

            Kind = kind,

            Content = content,

            Tags = tags.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),

            DocumentKey = doc != null ? _platform.GetDocumentKey(doc) : session.DocumentKey,

            ViewKey = viewKey,

            MissionId = session.Mission.MissionId,

            ToolName = toolName ?? string.Empty,

            CreatedUtc = _clock.UtcNow

        });

    }



    private WorkerChatMessage CreateMessage(string role, string content, string toolName = "", string statusCode = "")

    {

        return new WorkerChatMessage

        {

            MessageId = Guid.NewGuid().ToString("N"),

            Role = role,

            Content = content ?? string.Empty,

            ToolName = toolName ?? string.Empty,

            StatusCode = statusCode ?? string.Empty,

            TimestampUtc = _clock.UtcNow

        };

    }



    private static WorkerToolCard BuildToolCard(

        string toolName,

        string statusCode,

        bool succeeded,

        string summary,

        object payload,

        IEnumerable<string>? artifactRefs = null,

        string stage = "",

        double progress = 100,

        string whyThisTool = "",

        double confidence = 0.85d,

        IEnumerable<string>? recoveryHints = null,

        string executionTier = "",

        bool autoExecutionEligible = false)

    {

        return new WorkerToolCard

        {

            ToolName = toolName,

            StatusCode = statusCode,

            Succeeded = succeeded,

            Summary = summary ?? string.Empty,

            PayloadJson = SerializeToolCardPayload(toolName, summary, payload),

            ArtifactRefs = artifactRefs?.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? new List<string>(),

            Stage = WorkerFlowStages.Normalize(string.IsNullOrWhiteSpace(stage) ? (succeeded ? WorkerFlowStages.Done : WorkerFlowStages.Error) : stage),

            Progress = progress,

            WhyThisTool = string.IsNullOrWhiteSpace(whyThisTool) ? BuildWhyThisTool(toolName) : whyThisTool,

            Confidence = confidence,

            RecoveryHints = recoveryHints?.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? BuildDefaultRecoveryHints(toolName, succeeded),

            ExecutionTier = string.IsNullOrWhiteSpace(executionTier) ? InferExecutionTier(toolName) : executionTier,

            AutoExecutionEligible = autoExecutionEligible

        };

    }



    private static WorkerToolCard BuildContextToolCard(string toolName, string documentKey, string summary)

    {

        return new WorkerToolCard

        {

            ToolName = toolName,

            StatusCode = StatusCodes.ReadSucceeded,

            Succeeded = true,

            Summary = summary,

            PayloadJson = JsonUtil.Serialize(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)

            {

                ["DocumentKey"] = documentKey ?? string.Empty,

                ["Summary"] = summary ?? string.Empty

            }),

            Stage = WorkerFlowStages.Scan,

            Progress = 100,

            WhyThisTool = BuildWhyThisTool(toolName),

            Confidence = 0.95d,

            RecoveryHints = BuildDefaultRecoveryHints(toolName, true),

            ExecutionTier = WorkerExecutionTiers.Tier0,

            AutoExecutionEligible = true

        };

    }



    private ProjectContextBundleResponse GetProjectBundle(string workspaceId, string query, int maxSourceRefs = 4, int maxStandardsRefs = 4)

    {

        if (string.IsNullOrWhiteSpace(workspaceId))

        {

            return new ProjectContextBundleResponse

            {

                WorkspaceId = string.Empty,

                StatusCode = StatusCodes.ProjectContextNotInitialized,

                Exists = false

            };

        }



        return _copilotTasks.GetProjectContextBundle(GetToolCatalog(), new ProjectContextBundleRequest

        {

            WorkspaceId = workspaceId,

            Query = query ?? string.Empty,

            MaxSourceRefs = maxSourceRefs,

            MaxStandardsRefs = maxStandardsRefs

        });

    }



    private MemoryScopedSearchResponse SearchScopedResearchMemory(string query, string documentKey, string workspaceId, string intent)

    {

        var safeQuery = query ?? string.Empty;

        if (string.IsNullOrWhiteSpace(safeQuery))

        {

            return new MemoryScopedSearchResponse

            {

                Query = string.Empty,

                RetrievalScope = RetrievalScopes.DeliveryPath,

                Summary = "Khong co scoped memory hit nao phu hop."

            };

        }



        var hits = new List<ScopedMemoryHit>();

        var playbooks = _copilotTasks.MatchPlaybook(GetToolCatalog(), new PlaybookMatchRequest

        {

            WorkspaceId = workspaceId,

            Query = safeQuery,

            MaxResults = 2,

            PreferredCapabilityDomain = ResolveResearchMemoryCapabilityDomain(intent, safeQuery),

            Discipline = ResolveDiscipline(safeQuery, intent)

        });

        foreach (var match in playbooks.Matches)

        {

            hits.Add(new ScopedMemoryHit

            {

                Namespace = MemoryNamespaces.PlaybooksPolicies,

                Id = match.PlaybookId,

                Kind = "playbook",

                Title = match.PlaybookId,

                Snippet = match.Description,

                SourceRef = match.PackId,

                DocumentKey = documentKey,

                Score = Math.Round(match.Confidence * 100d, 2)

            });

        }



        var policy = _copilotTasks.ResolvePolicy(new PolicyResolutionRequest

        {

            WorkspaceId = workspaceId,

            CapabilityDomain = ResolveResearchMemoryCapabilityDomain(intent, safeQuery),

            Discipline = ResolveDiscipline(safeQuery, intent),

            IssueKinds = new List<string>()

        });

        foreach (var pack in policy.ResolvedPacks.Take(2))

        {

            hits.Add(new ScopedMemoryHit

            {

                Namespace = MemoryNamespaces.PlaybooksPolicies,

                Id = pack.PackId,

                Kind = "policy_pack",

                Title = pack.DisplayName,

                Snippet = pack.Description,

                SourceRef = pack.PackId,

                DocumentKey = documentKey,

                Score = 70

            });

        }



        var similarRuns = _copilotTasks.FindSimilarRuns(new MemoryFindSimilarRunsRequest

        {

            DocumentKey = documentKey,

            Query = safeQuery,

            TaskKind = "workflow",

            MaxResults = 3

        });

        foreach (var run in similarRuns.Runs)

        {

            var ns = string.Equals(run.Status, "verified", StringComparison.OrdinalIgnoreCase)

                || string.Equals(run.Status, "completed", StringComparison.OrdinalIgnoreCase)

                ? MemoryNamespaces.EvidenceLessons

                : MemoryNamespaces.ProjectRuntimeMemory;

            hits.Add(new ScopedMemoryHit

            {

                Namespace = ns,

                Id = run.RunId,

                Kind = "task_run",

                Title = string.IsNullOrWhiteSpace(run.TaskName) ? run.TaskKind : run.TaskName,

                Snippet = run.Summary,

                SourceRef = "task:" + run.RunId,

                DocumentKey = run.DocumentKey,

                Score = run.Score

            });

        }



        var ordered = hits

            .Where(x => ResolveResearchNamespaces().Contains(x.Namespace))

            .OrderByDescending(x => x.Score)

            .ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase)

            .Take(4)

            .ToList();

        return new MemoryScopedSearchResponse

        {

            Query = safeQuery,

            RetrievalScope = RetrievalScopes.DeliveryPath,

            Hits = ordered,

            Summary = ordered.Count == 0

                ? "Khong co scoped memory hit nao phu hop."

                : $"Scoped memory resolved {ordered.Count} hit(s) across {string.Join(", ", ordered.Select(x => x.Namespace).Distinct(StringComparer.OrdinalIgnoreCase))}."

        };

    }



    private static IReadOnlyCollection<string> ResolveResearchNamespaces()

    {

        return new[]

        {

            MemoryNamespaces.PlaybooksPolicies,

            MemoryNamespaces.ProjectRuntimeMemory,

            MemoryNamespaces.EvidenceLessons

        };

    }



    private static string ResolveResearchMemoryCapabilityDomain(string intent, string query)

    {

        var normalized = (query ?? string.Empty).ToLowerInvariant();

        if (string.Equals(intent, "project_research_request", StringComparison.OrdinalIgnoreCase)

            && (normalized.Contains("sheet") || normalized.Contains("ban ve")))

        {

            return CapabilityDomains.Governance;

        }



        if (normalized.Contains("clash") || normalized.Contains("opening") || normalized.Contains("xung dot"))

        {

            return CapabilityDomains.Coordination;

        }



        return CapabilityDomains.General;

    }



    private static bool ShouldResolveStandardsEvidence(string query, ProjectContextBundleResponse bundle)

    {

        if ((bundle?.TopStandardsRefs?.Count ?? 0) > 0)

        {

            return true;

        }



        var normalized = (query ?? string.Empty).ToLowerInvariant();

        return normalized.Contains("standard")

               || normalized.Contains("policy")

               || normalized.Contains("template")

               || normalized.Contains("spec")

               || normalized.Contains("naming")

               || normalized.Contains("iso");

    }



    private static string ResolveStandardsKindForResearch(string query)

    {

        var normalized = (query ?? string.Empty).ToLowerInvariant();

        if (normalized.Contains("naming"))

        {

            return "naming";

        }



        if (normalized.Contains("template"))

        {

            return "template";

        }



        if (normalized.Contains("spec"))

        {

            return "spec";

        }



        return string.Empty;

    }



    private static List<string> BuildStandardsRequestedKeys(string query)

    {

        var normalized = (query ?? string.Empty).ToLowerInvariant();

        var keys = new List<string>();

        if (normalized.Contains("naming"))

        {

            keys.Add("naming");

        }



        if (normalized.Contains("sheet"))

        {

            keys.Add("sheet");

        }



        if (normalized.Contains("family"))

        {

            keys.Add("family");

        }



        if (normalized.Contains("parameter"))

        {

            keys.Add("parameter");

        }



        return keys;

    }



    private static void ApplyResearchGroundingMetadata(

        WorkerContextSummary contextSummary,

        ProjectContextBundleResponse bundle,

        ProjectDeepScanReportResponse? deepScan,

        ArtifactSummaryResponse? deepScanArtifact,

        StandardsResolution? standards,

        MemoryScopedSearchResponse? scopedMemory)

    {

        contextSummary ??= new WorkerContextSummary();

        var safeBundle = bundle ?? new ProjectContextBundleResponse();

        if (!safeBundle.Exists)

        {

            contextSummary.GroundingLevel = WorkerGroundingLevels.LiveContextOnly;

            contextSummary.GroundingSummary = "Dang dung live Revit context; project context chua init.";

            contextSummary.GroundingRefs = new List<string>();

            return;

        }



        contextSummary.GroundingLevel = deepScan?.Exists == true || IsDeepScanGroundedStatus(safeBundle.DeepScanStatus)

            ? WorkerGroundingLevels.DeepScanGrounded

            : WorkerGroundingLevels.WorkspaceGrounded;

        contextSummary.GroundingSummary = contextSummary.GroundingLevel == WorkerGroundingLevels.DeepScanGrounded

            ? FirstNonEmpty(deepScanArtifact?.Summary, deepScan?.Summary, safeBundle.DeepScanSummary, safeBundle.Summary, "Grounded by workspace + deep scan.")

            : FirstNonEmpty(safeBundle.Summary, "Grounded by workspace context bundle.");



        var refs = new List<string>();

        foreach (var reference in (safeBundle.TopStandardsRefs ?? new List<ProjectContextRef>())

                     .Concat(safeBundle.SourceRefs ?? new List<ProjectContextRef>())

                     .Concat(safeBundle.DeepScanRefs ?? new List<ProjectContextRef>()))

        {

            AddDistinctIgnoreCase(refs, ResolveRefLabel(reference));

        }



        AddDistinctIgnoreCase(refs, deepScan?.SummaryReportPath ?? string.Empty);

        AddDistinctIgnoreCase(refs, deepScan?.ReportPath ?? string.Empty);

        foreach (var file in standards?.Files?.Take(3) ?? Enumerable.Empty<StandardsResolvedFile>())

        {

            AddDistinctIgnoreCase(refs, file.RelativePath);

        }



        foreach (var hit in scopedMemory?.Hits?.Take(2) ?? Enumerable.Empty<ScopedMemoryHit>())

        {

            AddDistinctIgnoreCase(refs, hit.SourceRef);

        }



        contextSummary.GroundingRefs = refs;

    }



    private SkillCaptureProposal BuildSkillCaptureProposal(

        WorkerConversationSessionState session,

        string workspaceId,

        PlaybookRecommendation selectedPlaybook,

        IEnumerable<string> artifactRefs,

        IEnumerable<WorkerToolCard> toolCards,

        string executionTier)

    {

        var safePlaybook = selectedPlaybook ?? new PlaybookRecommendation();

        var safeArtifacts = (artifactRefs ?? Enumerable.Empty<string>())

            .Where(x => !string.IsNullOrWhiteSpace(x))

            .Distinct(StringComparer.OrdinalIgnoreCase)

            .ToList();

        var manifestMap = GetToolCatalog()

            .Where(x => !string.IsNullOrWhiteSpace(x.ToolName))

            .GroupBy(x => x.ToolName, StringComparer.OrdinalIgnoreCase)

            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

        var teachableTools = (toolCards ?? Enumerable.Empty<WorkerToolCard>())

            .Select(x => x.ToolName)

            .Where(x => !string.IsNullOrWhiteSpace(x))

            .Where(x => manifestMap.TryGetValue(x, out var manifest) && manifest.CanTeachBack)

            .Distinct(StringComparer.OrdinalIgnoreCase)

            .ToList();



        if (string.IsNullOrWhiteSpace(safePlaybook.PlaybookId) && safeArtifacts.Count == 0 && teachableTools.Count == 0)

        {

            return new SkillCaptureProposal();

        }



        var captureKey = !string.IsNullOrWhiteSpace(safePlaybook.PlaybookId)

            ? safePlaybook.PlaybookId

            : teachableTools.FirstOrDefault() ?? "workflow";



        return new SkillCaptureProposal

        {

            SourceRunId = session?.Mission?.MissionId ?? string.Empty,

            WorkspaceId = workspaceId ?? string.Empty,

            Summary = "Luu luong cong viec nay thanh reusable skill de lan sau replay gan zero-token.",

            CandidateSkillId = $"skill.{SlugifyValue(workspaceId)}.{SlugifyValue(captureKey)}.{_clock.UtcNow:yyyyMMdd}",

            PlaybookId = safePlaybook.PlaybookId ?? string.Empty,

            ArtifactRefs = safeArtifacts,

            CacheValueClass = safeArtifacts.Count > 0 ? CacheValueClasses.ArtifactReuse : CacheValueClasses.TeachBack,

            CanPromoteToFreeReplay = safeArtifacts.Count > 0 || string.Equals(executionTier, WorkerExecutionTiers.Tier0, StringComparison.OrdinalIgnoreCase),

            CommercialTier = string.IsNullOrWhiteSpace(safePlaybook.PlaybookId) && safeArtifacts.Count == 0

                ? CommercialTiers.Free

                : CommercialTiers.PersonalPro,

            Confidence = safeArtifacts.Count > 0 || !string.IsNullOrWhiteSpace(safePlaybook.PlaybookId) ? 0.86d : 0.74d

        };

    }



    private static ProjectPatternSnapshot BuildProjectPatternSnapshot(

        ProjectContextBundleResponse bundle,

        PlaybookRecommendation selectedPlaybook,

        CompiledTaskPlan compiledPlan,

        IEnumerable<WorkerToolCard> toolCards,

        string discipline,

        string workspaceId)

    {

        var safeBundle = bundle ?? new ProjectContextBundleResponse();

        var safePlaybook = selectedPlaybook ?? new PlaybookRecommendation();

        var refs = EnumerateProjectRefs(safeBundle).ToList();

        var recommendedPlaybooks = new List<string>();

        AddDistinctIgnoreCase(recommendedPlaybooks, safePlaybook.PlaybookId);

        AddDistinctIgnoreCase(recommendedPlaybooks, safeBundle.RecommendedPlaybookId);



        var recommendedToolNames = (compiledPlan?.CandidateToolNames ?? new List<string>())

            .Concat(compiledPlan?.VerifyTools ?? new List<string>())

            .Concat((toolCards ?? Enumerable.Empty<WorkerToolCard>()).Select(x => x.ToolName))

            .Where(x => !string.IsNullOrWhiteSpace(x))

            .Distinct(StringComparer.OrdinalIgnoreCase)

            .Take(8)

            .ToList();



        if (!safeBundle.Exists && recommendedPlaybooks.Count == 0 && recommendedToolNames.Count == 0)

        {

            return new ProjectPatternSnapshot();

        }



        return new ProjectPatternSnapshot

        {

            WorkspaceId = string.IsNullOrWhiteSpace(safeBundle.WorkspaceId) ? workspaceId : safeBundle.WorkspaceId,

            Discipline = string.IsNullOrWhiteSpace(discipline) ? CapabilityDisciplines.Common : discipline,

            RecommendedPlaybooks = recommendedPlaybooks,

            RecommendedToolNames = recommendedToolNames,

            ParameterMappingRefs = refs

                .Where(x => HasRefHint(x, "mapping", "parameter", "csv", "openxml", "schedule"))

                .Select(ResolveRefLabel)

                .Where(x => !string.IsNullOrWhiteSpace(x))

                .Distinct(StringComparer.OrdinalIgnoreCase)

                .Take(6)

                .ToList(),

            ExportProfileRefs = refs

                .Where(x => HasRefHint(x, "export", "profile", "pdf", "dwg", "ifc", "print"))

                .Select(ResolveRefLabel)

                .Where(x => !string.IsNullOrWhiteSpace(x))

                .Distinct(StringComparer.OrdinalIgnoreCase)

                .Take(6)

                .ToList(),

            Summary = string.IsNullOrWhiteSpace(safeBundle.Summary)

                ? $"Pattern snapshot: {recommendedPlaybooks.Count} playbook, {recommendedToolNames.Count} tool, {refs.Count} project ref."

                : safeBundle.Summary,

            Confidence = safeBundle.Exists ? 0.78d : 0.62d,

            SourceWorkspaceId = string.IsNullOrWhiteSpace(safeBundle.WorkspaceId) ? workspaceId : safeBundle.WorkspaceId

        };

    }



    private static TemplateSynthesisProposal BuildTemplateSynthesisProposal(ProjectContextBundleResponse bundle, ProjectPatternSnapshot snapshot)

    {

        var safeBundle = bundle ?? new ProjectContextBundleResponse();

        var safeSnapshot = snapshot ?? new ProjectPatternSnapshot();

        if (!safeBundle.Exists

            || string.IsNullOrWhiteSpace(safeBundle.DeepScanStatus)

            || string.Equals(safeBundle.DeepScanStatus, ProjectDeepScanStatuses.NotStarted, StringComparison.OrdinalIgnoreCase))

        {

            return new TemplateSynthesisProposal();

        }



        var workspaceId = string.IsNullOrWhiteSpace(safeBundle.WorkspaceId) ? safeSnapshot.WorkspaceId : safeBundle.WorkspaceId;

        var slug = SlugifyValue(workspaceId);



        return new TemplateSynthesisProposal

        {

            WorkspaceId = workspaceId,

            SourceProjectWorkspaceId = workspaceId,

            Summary = $"Đề xuất starter pack cho workspace {workspaceId} từ project brief + deep scan + lịch sử playbook.",

            Confidence = string.Equals(safeBundle.DeepScanStatus, ProjectDeepScanStatuses.Completed, StringComparison.OrdinalIgnoreCase) ? 0.84d : 0.72d,

            ProposedWorkspacePackId = $"bim765t.generated.{slug}.starter",

            ProposedArtifactPaths = new List<string>

            {

                $"artifacts/templates/{slug}.starter.playbook.json",

                $"artifacts/templates/{slug}.parameter_mapping.json",

                $"artifacts/templates/{slug}.export_profile.json"

            },

            VerificationRecipe = "review starter pack -> compare with current standards refs -> approve import into workspace packs",

            RequiresApproval = true,

            Snapshot = safeSnapshot,

            CommercialTier = CommercialTiers.StudioAutopilot

        };

    }



    private static List<DeltaSuggestion> BuildDeltaSuggestions(

        ProjectContextBundleResponse bundle,

        WorkerContextSummary contextSummary,

        PendingApprovalRef pendingApproval,

        IEnumerable<WorkerToolCard> toolCards,

        PlaybookRecommendation selectedPlaybook,

        string workspaceId)

    {

        var suggestions = new List<DeltaSuggestion>();

        var safeBundle = bundle ?? new ProjectContextBundleResponse();

        var safeContext = contextSummary ?? new WorkerContextSummary();

        var safePlaybook = selectedPlaybook ?? new PlaybookRecommendation();



        if (pendingApproval != null && !string.IsNullOrWhiteSpace(pendingApproval.PendingActionId))

        {

            suggestions.Add(new DeltaSuggestion

            {

                WorkspaceId = workspaceId ?? string.Empty,

                Summary = string.IsNullOrWhiteSpace(pendingApproval.Summary)

                    ? "Đang có preview pending cần resume approval."

                    : pendingApproval.Summary,

                Reason = "pending_approval",

                Stage = WorkerFlowStages.Approval,

                CandidateToolNames = new List<string> { ToolNames.CommandExecuteSafe },

                CandidatePlaybookId = safePlaybook.PlaybookId ?? string.Empty,

                RequiresApproval = true,

                Confidence = 0.96d,

                WatchRuleId = WatchTriggerKinds.ReopenSummary

            });

        }



        if (!safeBundle.Exists)

        {

            suggestions.Add(new DeltaSuggestion

            {

                WorkspaceId = workspaceId ?? string.Empty,

                Summary = "Khởi tạo project context trước khi worker save hoặc replay skill lane.",

                Reason = "project_init_pending",

                Stage = WorkerFlowStages.Plan,

                CandidateToolNames = new List<string> { ToolNames.ProjectInitPreview, ToolNames.ProjectInitApply },

                Confidence = 0.9d,

                WatchRuleId = WatchTriggerKinds.ReopenSummary

            });

            return suggestions;

        }



        if (string.IsNullOrWhiteSpace(safeBundle.DeepScanStatus)

            || string.Equals(safeBundle.DeepScanStatus, ProjectDeepScanStatuses.NotStarted, StringComparison.OrdinalIgnoreCase))

        {

            suggestions.Add(new DeltaSuggestion

            {

                WorkspaceId = safeBundle.WorkspaceId,

                Summary = "Chạy project.deep_scan để worker có pattern và evidence tốt hơn.",

                Reason = "deep_scan_pending",

                Stage = WorkerFlowStages.Scan,

                CandidateToolNames = new List<string> { ToolNames.ProjectDeepScan },

                CandidatePlaybookId = safePlaybook.PlaybookId ?? safeBundle.RecommendedPlaybookId,

                Confidence = 0.88d,

                WatchRuleId = WatchTriggerKinds.DeltaScan

            });

        }



        if ((safeContext.ProjectPendingUnknowns?.Count ?? 0) > 0)

        {

            suggestions.Add(new DeltaSuggestion

            {

                WorkspaceId = safeBundle.WorkspaceId,

                Summary = "Van con unknowns trong standards/context; resolve som de playbook chain on dinh hon.",

                Reason = "context_unknowns",

                Stage = WorkerFlowStages.Plan,

                CandidateToolNames = new List<string> { ToolNames.StandardsResolve },

                CandidatePlaybookId = safePlaybook.PlaybookId ?? safeBundle.RecommendedPlaybookId,

                Confidence = 0.79d,

                WatchRuleId = WatchTriggerKinds.DeliverableDrift

            });

        }



        foreach (var toolName in (safeContext.SuggestedNextTools ?? new List<string>())

                     .Concat((toolCards ?? Enumerable.Empty<WorkerToolCard>()).Select(x => x.ToolName))

                     .Where(x => !string.IsNullOrWhiteSpace(x))

                     .Distinct(StringComparer.OrdinalIgnoreCase)

                     .Take(3))

        {

            suggestions.Add(new DeltaSuggestion

            {

                WorkspaceId = safeBundle.WorkspaceId,

                Summary = $"Neu can tiep tuc, uu tien `{toolName}` theo context hien tai.",

                Reason = "context_next_tool",

                Stage = WorkerFlowStages.Thinking,

                CandidateToolNames = new List<string> { toolName },

                CandidatePlaybookId = safePlaybook.PlaybookId ?? safeBundle.RecommendedPlaybookId,

                Confidence = 0.68d,

                WatchRuleId = WatchTriggerKinds.DeltaScan

            });

        }



        return suggestions

            .GroupBy(x => $"{x.Reason}:{x.Summary}", StringComparer.OrdinalIgnoreCase)

            .Select(x => x.First())

            .Take(5)

            .ToList();

    }



    private static FallbackArtifactProposal ResolveFallbackProposal(IEnumerable<WorkerToolCard> toolCards)

    {

        foreach (var card in toolCards ?? Enumerable.Empty<WorkerToolCard>())

        {

            if (string.IsNullOrWhiteSpace(card.PayloadJson))

            {

                continue;

            }



            if (string.Equals(card.ToolName, ToolNames.WorkflowQuickPlan, StringComparison.OrdinalIgnoreCase))

            {

                var payload = JsonUtil.DeserializePayloadOrDefault<QuickActionResponse>(card.PayloadJson);

                var fallback = payload?.FallbackProposal;

                if (HasProposal(fallback))

                {

                    return fallback!;

                }

            }



            if (string.Equals(card.ToolName, ToolNames.CommandExecuteSafe, StringComparison.OrdinalIgnoreCase))

            {

                var payload = JsonUtil.DeserializePayloadOrDefault<CommandExecuteResponse>(card.PayloadJson);

                var fallback = payload?.FallbackProposal;

                if (HasProposal(fallback))

                {

                    return fallback!;

                }

            }

        }



        return new FallbackArtifactProposal();

    }



    private static bool HasProposal(FallbackArtifactProposal? proposal)

    {

        return proposal != null

               && (((proposal.ArtifactKinds?.Count ?? 0) > 0)

                   || (proposal.ArtifactPaths?.Count ?? 0) > 0);

    }



    private static void AppendFallbackActionCard(ICollection<WorkerActionCard> actionCards, FallbackArtifactProposal proposal, bool isPrimary = false)

    {

        if (!HasProposal(proposal)

            || actionCards.Any(x => string.Equals(x.ToolName, ToolNames.FallbackArtifactPlan, StringComparison.OrdinalIgnoreCase)))

        {

            return;

        }



        actionCards.Add(new WorkerActionCard

        {

            ActionKind = WorkerActionKinds.Suggest,

            Title = "Review fallback artifact",

            Summary = string.IsNullOrWhiteSpace(proposal.PreviewSummary) ? proposal.Summary : proposal.PreviewSummary,

            ToolName = ToolNames.FallbackArtifactPlan,

            PayloadJson = JsonUtil.Serialize(ToFallbackArtifactRequest(proposal)),

            IsPrimary = isPrimary && !actionCards.Any(x => x.IsPrimary),

            ExecutionTier = WorkerExecutionTiers.Tier0,

            WhyThisAction = "Atlas miss hoac mapped-only nen worker de xuat artifact fallback an toan thay vi sinh code tu do.",

            Confidence = 0.82d,

            RecoveryHint = "Review proposal, giu playbook/dry-run lane truoc khi promote thanh reusable skill.",

            AutoExecutionEligible = false

        });

    }



    private static void AppendSkillCaptureActionCard(ICollection<WorkerActionCard> actionCards, SkillCaptureProposal proposal)

    {

        if (proposal == null

            || string.IsNullOrWhiteSpace(proposal.CandidateSkillId)

            || actionCards.Any(x => string.Equals(x.ToolName, ToolNames.MemoryPromoteVerifiedRun, StringComparison.OrdinalIgnoreCase)))

        {

            return;

        }



        actionCards.Add(new WorkerActionCard

        {

            ActionKind = WorkerActionKinds.Suggest,

            Title = "Save reusable skill",

            Summary = proposal.Summary,

            ToolName = ToolNames.MemoryPromoteVerifiedRun,

            PayloadJson = JsonUtil.Serialize(proposal),

            IsPrimary = !actionCards.Any(x => x.IsPrimary),

            ExecutionTier = WorkerExecutionTiers.Tier0,

            WhyThisAction = "Save lan chay thanh reusable skill de lan sau replay nhanh hon va ton it token hon.",

            Confidence = Math.Max(0.7d, proposal.Confidence),

            RecoveryHint = "Chi promote khi workflow vua chay ra ket qua dung va artifacts/evidence da on.",

            AutoExecutionEligible = false

        });

    }



    private static FallbackArtifactRequest ToFallbackArtifactRequest(FallbackArtifactProposal proposal)

    {

        var safeProposal = proposal ?? new FallbackArtifactProposal();

        var query = proposal?.InputsUsed?

            .FirstOrDefault(x => x.StartsWith("query=", StringComparison.OrdinalIgnoreCase))

            ?.Substring("query=".Length) ?? string.Empty;

        var candidateBuiltInToolName = safeProposal.CandidateBuiltInToolName ?? string.Empty;

        var candidatePlaybookId = safeProposal.CandidatePlaybookId ?? string.Empty;



        return new FallbackArtifactRequest

        {

            WorkspaceId = safeProposal.WorkspaceId ?? string.Empty,

            Query = query,

            Reason = safeProposal.Reason ?? string.Empty,

            RequestedKinds = safeProposal.ArtifactKinds?.ToList() ?? new List<string>(),

            CandidateToolNames = string.IsNullOrWhiteSpace(candidateBuiltInToolName)

                ? new List<string>()

                : new List<string> { candidateBuiltInToolName },

            CandidatePlaybookIds = string.IsNullOrWhiteSpace(candidatePlaybookId)

                ? new List<string>()

                : new List<string> { candidatePlaybookId },

            ExistingArtifactRefs = safeProposal.ArtifactPaths?.ToList() ?? new List<string>(),

            InputSummary = safeProposal.Summary ?? string.Empty,

            CommercialTier = safeProposal.CommercialTier ?? CommercialTiers.PersonalPro

        };

    }



    private static IEnumerable<ProjectContextRef> EnumerateProjectRefs(ProjectContextBundleResponse bundle)

    {

        return (bundle?.TopStandardsRefs ?? new List<ProjectContextRef>())

            .Concat(bundle?.SourceRefs ?? new List<ProjectContextRef>())

            .Concat(bundle?.DeepScanRefs ?? new List<ProjectContextRef>());

    }



    private static bool HasRefHint(ProjectContextRef reference, params string[] hints)

    {

        var haystack = string.Join(" ",

            new[]

            {

                reference?.Title,

                reference?.RefKind,

                reference?.RelativePath,

                reference?.SourcePath,

                reference?.Summary

            }.Where(x => !string.IsNullOrWhiteSpace(x))).ToLowerInvariant();



        return hints.Any(hint => haystack.Contains((hint ?? string.Empty).ToLowerInvariant()));

    }



    private static string ResolveRefLabel(ProjectContextRef? reference)

    {

        var relativePath = reference?.RelativePath ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(relativePath))

        {

            return relativePath;

        }



        var sourcePath = reference?.SourcePath ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(sourcePath))

        {

            return sourcePath;

        }



        return reference?.RefId ?? string.Empty;

    }



    private static string SlugifyValue(string? value)

    {

        var slug = Regex.Replace((value ?? string.Empty).Trim().ToLowerInvariant(), @"[^a-z0-9]+", ".");

        slug = slug.Trim('.');

        return string.IsNullOrWhiteSpace(slug) ? "workspace" : slug;

    }



    private static void AddDistinctIgnoreCase(ICollection<string> values, string value)

    {

        if (!string.IsNullOrWhiteSpace(value) && !values.Contains(value, StringComparer.OrdinalIgnoreCase))

        {

            values.Add(value);

        }

    }



    private static string FirstNonEmpty(params string?[] values)

    {

        foreach (var value in values ?? Array.Empty<string?>())

        {

            if (!string.IsNullOrWhiteSpace(value))

            {

                return value!.Trim();

            }

        }



        return string.Empty;

    }



    private static PendingApprovalRef ToPendingApprovalRef(WorkerPendingApprovalState pending)

    {

        if (pending == null || !pending.HasPendingApproval)

        {

            return new PendingApprovalRef();

        }



        return new PendingApprovalRef

        {

            PendingActionId = pending.PendingActionId,

            ToolName = pending.ToolName,

            Summary = pending.Summary,

            ExpiresUtc = pending.ExpiresUtc,

            ExecutionTier = InferExecutionTier(pending.ToolName),

            RecoveryHint = "Neu context drift hoac token het han, hay preview lai truoc khi execute.",

            AutoExecutionEligible = pending.AutoExecutionEligible

        };

    }



    private static string SerializePayload(object payload)

    {

        if (payload == null)

        {

            return string.Empty;

        }



        var serializeMethod = typeof(JsonUtil)

            .GetMethods(BindingFlags.Public | BindingFlags.Static)

            .First(method => string.Equals(method.Name, nameof(JsonUtil.Serialize), StringComparison.Ordinal) && method.IsGenericMethodDefinition);

        var closedMethod = serializeMethod.MakeGenericMethod(payload.GetType());

        return (string)(closedMethod.Invoke(null, new[] { payload }) ?? string.Empty);

    }



    private static string SerializeToolCardPayload(string toolName, string? summary, object payload)

    {

        if (payload == null)

        {

            return string.Empty;

        }



        try

        {

            return SerializePayload(payload);

        }

        catch

        {

            return JsonUtil.Serialize(new Dictionary<string, string>

            {

                ["toolName"] = toolName ?? string.Empty,

                ["summary"] = summary ?? string.Empty,

                ["payloadType"] = payload.GetType().FullName ?? payload.GetType().Name,

                ["serializationStatus"] = "fallback_summary_only"

            });

        }

    }



    private ToolRequestEnvelope BuildWorkerToolEnvelope(WorkerConversationSessionState session, ToolRequestEnvelope source, string toolName, string payloadJson, bool dryRun, string approvalToken, string previewRunId, string expectedContextJson)

    {

        return new ToolRequestEnvelope

        {

            RequestId = Guid.NewGuid().ToString("N"),

            ToolName = toolName,

            PayloadJson = payloadJson,

            Caller = string.IsNullOrWhiteSpace(source.Caller) ? "worker" : source.Caller,

            SessionId = session.SessionId,

            DryRun = dryRun,

            TargetDocument = string.IsNullOrWhiteSpace(source.TargetDocument) ? session.DocumentKey : source.TargetDocument,

            TargetView = source.TargetView,

            ExpectedContextJson = expectedContextJson ?? string.Empty,

            ApprovalToken = approvalToken ?? string.Empty,

            ScopeDescriptorJson = source.ScopeDescriptorJson,

            RequestedAtUtc = _clock.UtcNow,

            PreviewRunId = previewRunId ?? string.Empty,

            CorrelationId = string.IsNullOrWhiteSpace(source.CorrelationId) ? Guid.NewGuid().ToString("N") : source.CorrelationId,

            ProtocolVersion = source.ProtocolVersion

        };

    }



    private static string BuildTaskContextSummary(TaskContextResponse taskContext)

    {

        var safeTaskContext = taskContext ?? new TaskContextResponse();

        var viewName = safeTaskContext.ActiveContext?.ViewName ?? string.Empty;

        return $"Live context: document '{safeTaskContext.Document?.Title}', view '{viewName}', selection {(safeTaskContext.Selection?.Count ?? 0)}.";

    }



    private static string BuildContextResponseText(WorkerContextSummary summary)

    {

        return BuildGroundedResearchResponseText(summary, new ProjectContextBundleResponse(), null, null, null);

    }



    private static string BuildGreetingResponseText(WorkerContextSummary summary)

    {

        var documentTitle = ResolveDocumentTitle(summary);

        var activeView = ResolveActiveViewName(summary);

        return $"Ch?o anh. Em ?ang ? file '{documentTitle}', view '{activeView}'. Anh mu?n em ki?m tra context, r? QC model, hay xem family/sheet tr??c?";

    }



    private static string BuildIdentityResponseText(WorkerContextSummary summary)

    {

        var documentTitle = ResolveDocumentTitle(summary);

        var activeView = ResolveActiveViewName(summary);

        return $"Em l? 765T Worker, tr? l? BIM ch?y tr?c ti?p trong Revit. Hi?n em ?ang ? '{documentTitle}' t?i '{activeView}', v? c? th? ??c context, r? QC read-only, xem family, sheet, ho?c view cho anh.";

    }



    private static string BuildHelpResponseText(WorkerContextSummary summary)

    {

        var documentTitle = ResolveDocumentTitle(summary);

        var activeView = ResolveActiveViewName(summary);

        return $"Em ?ang ? '{documentTitle}' t?i '{activeView}'. Anh c? th? giao ng?n g?n nh? xem context, QC model, ph?n t?ch family, ho?c review sheet; em s? t? ??c ng? c?nh r?i x? l? theo ??ng h??ng.";

    }



    private static string ResolveDocumentTitle(WorkerContextSummary summary)

    {

        return string.IsNullOrWhiteSpace(summary.DocumentTitle) ? "model hi?n t?i" : summary.DocumentTitle;

    }



    private static string ResolveActiveViewName(WorkerContextSummary summary)

    {

        return string.IsNullOrWhiteSpace(summary.ActiveViewName) ? "view ?ang active" : summary.ActiveViewName;

    }



    private static string BuildGroundedResearchResponseText(

        WorkerContextSummary summary,

        ProjectContextBundleResponse bundle,

        ProjectDeepScanReportResponse? deepScan,

        StandardsResolution? standards,

        MemoryScopedSearchResponse? scopedMemory)

    {

        summary ??= new WorkerContextSummary();

        bundle ??= new ProjectContextBundleResponse();

        var grounding = string.IsNullOrWhiteSpace(summary.GroundingLevel) ? WorkerGroundingLevels.LiveContextOnly : summary.GroundingLevel;

        var lead = grounding switch

        {

            WorkerGroundingLevels.DeepScanGrounded => "Em dang grounded theo Project Brain deep scan + workspace context.",

            WorkerGroundingLevels.WorkspaceGrounded => "Em dang grounded theo workspace context bundle.",

            _ => "Em dang tra loi tu live Revit context."

        };

        var contextPart = $" Document '{summary.DocumentTitle}', view '{summary.ActiveViewName}', selection {summary.SelectionCount}.";

        var deltaPart = string.IsNullOrWhiteSpace(summary.Summary) ? string.Empty : $" Delta: {summary.Summary}.";

        var projectPart = string.IsNullOrWhiteSpace(summary.ProjectSummary) ? string.Empty : $" Project: {summary.ProjectSummary}.";

        var deepScanPart = deepScan?.Exists == true && deepScan.Report != null

            ? $" Deep scan: {deepScan.Report.Stats?.FindingCount ?? deepScan.Report.Findings?.Count ?? 0} finding(s), {deepScan.Report.Stats?.SheetsScanned ?? 0} sheet(s), {deepScan.Report.Stats?.LoadedLinks ?? 0}/{deepScan.Report.Stats?.TotalLinks ?? 0} link(s) loaded."

            : string.Empty;

        var standardsPart = standards != null && ((standards.Files?.Count ?? 0) > 0 || (standards.Values?.Count ?? 0) > 0)

            ? $" Standards: {standards.Summary}."

            : string.Empty;

        var memoryPart = scopedMemory != null && (scopedMemory.Hits?.Count ?? 0) > 0

            ? $" Memory: {scopedMemory.Summary}."

            : string.Empty;

        var cta = grounding switch

        {

            WorkerGroundingLevels.LiveContextOnly => " Bam Init workspace de co workspace-grounded context.",

            WorkerGroundingLevels.WorkspaceGrounded => " Bam Run deep scan de co evidence sau hon.",

            _ => string.Empty

        };



        return (lead + contextPart + deltaPart + projectPart + deepScanPart + standardsPart + memoryPart + cta).Trim();

    }



    private static bool IsDeepScanGroundedStatus(string? status)

    {

        return string.Equals(status, ProjectDeepScanStatuses.Completed, StringComparison.OrdinalIgnoreCase)

            || string.Equals(status, ProjectDeepScanStatuses.Partial, StringComparison.OrdinalIgnoreCase);

    }



    private static bool ShouldResolveCapabilityPlanning(string? intent)

    {

        return !string.Equals(intent, "greeting", StringComparison.OrdinalIgnoreCase)

               && !string.Equals(intent, "identity_query", StringComparison.OrdinalIgnoreCase)

               && !string.Equals(intent, "help", StringComparison.OrdinalIgnoreCase)

               && !string.Equals(intent, "context_query", StringComparison.OrdinalIgnoreCase)

               && !string.Equals(intent, "project_research_request", StringComparison.OrdinalIgnoreCase)

               && !string.Equals(intent, "qc_request", StringComparison.OrdinalIgnoreCase)

               && !string.Equals(intent, "sheet_analysis_request", StringComparison.OrdinalIgnoreCase)

               && !string.Equals(intent, "family_analysis_request", StringComparison.OrdinalIgnoreCase)

               && !string.Equals(intent, "mutation_request", StringComparison.OrdinalIgnoreCase)

               && !string.Equals(intent, "approval", StringComparison.OrdinalIgnoreCase)

               && !string.Equals(intent, "reject", StringComparison.OrdinalIgnoreCase)

               && !string.Equals(intent, "resume", StringComparison.OrdinalIgnoreCase)

               && !string.Equals(intent, "cancel", StringComparison.OrdinalIgnoreCase);

    }



    private static string ResolveStage(string missionStatus)

    {

        if (string.Equals(missionStatus, WorkerMissionStates.Completed, StringComparison.OrdinalIgnoreCase))

        {

            return WorkerFlowStages.Done;

        }



        if (string.Equals(missionStatus, WorkerMissionStates.AwaitingApproval, StringComparison.OrdinalIgnoreCase))

        {

            return WorkerFlowStages.Approval;

        }



        if (string.Equals(missionStatus, WorkerMissionStates.Running, StringComparison.OrdinalIgnoreCase))

        {

            return WorkerFlowStages.Run;

        }



        if (string.Equals(missionStatus, WorkerMissionStates.Verifying, StringComparison.OrdinalIgnoreCase))

        {

            return WorkerFlowStages.Verify;

        }



        if (string.Equals(missionStatus, WorkerMissionStates.Blocked, StringComparison.OrdinalIgnoreCase)

            || string.Equals(missionStatus, WorkerMissionStates.Failed, StringComparison.OrdinalIgnoreCase))

        {

            return WorkerFlowStages.Error;

        }



        if (string.Equals(missionStatus, WorkerMissionStates.Planned, StringComparison.OrdinalIgnoreCase))

        {

            return WorkerFlowStages.Plan;

        }



        if (string.Equals(missionStatus, WorkerMissionStates.Understanding, StringComparison.OrdinalIgnoreCase))

        {

            return WorkerFlowStages.Thinking;

        }



        return WorkerFlowStages.Thinking;

    }



    private static double ResolveMissionProgress(string missionStatus, WorkerPendingApprovalState pendingApproval, QueueStateResponse queueState)

    {

        if (string.Equals(missionStatus, WorkerMissionStates.Completed, StringComparison.OrdinalIgnoreCase))

        {

            return 100d;

        }



        if (string.Equals(missionStatus, WorkerMissionStates.Verifying, StringComparison.OrdinalIgnoreCase))

        {

            return 90d;

        }



        if (string.Equals(missionStatus, WorkerMissionStates.Running, StringComparison.OrdinalIgnoreCase))

        {

            return queueState.HasActiveInvocation ? 75d : 70d;

        }



        if (pendingApproval != null && pendingApproval.HasPendingApproval)

        {

            return 60d;

        }



        if (string.Equals(missionStatus, WorkerMissionStates.Planned, StringComparison.OrdinalIgnoreCase))

        {

            return 40d;

        }



        if (string.Equals(missionStatus, WorkerMissionStates.Understanding, StringComparison.OrdinalIgnoreCase))

        {

            return 25d;

        }



        if (string.Equals(missionStatus, WorkerMissionStates.Blocked, StringComparison.OrdinalIgnoreCase)

            || string.Equals(missionStatus, WorkerMissionStates.Failed, StringComparison.OrdinalIgnoreCase))

        {

            return 15d;

        }



        return 10d;

    }



    private static double ResolveConfidence(string intent, IEnumerable<WorkerToolCard> toolCards, IEnumerable<WorkerActionCard> actionCards)

    {

        if (string.Equals(intent, "help", StringComparison.OrdinalIgnoreCase)

            || string.Equals(intent, "greeting", StringComparison.OrdinalIgnoreCase))

        {

            return 0.95d;

        }



        if (toolCards.Any(x => !x.Succeeded))

        {

            return 0.55d;

        }



        if (actionCards.Any(x => string.Equals(x.ActionKind, WorkerActionKinds.Clarify, StringComparison.OrdinalIgnoreCase)))

        {

            return 0.68d;

        }



        if (string.Equals(intent, "mutation_request", StringComparison.OrdinalIgnoreCase))

        {

            return 0.72d;

        }



        return 0.86d;

    }



    private static List<string> BuildRecoveryHints(WorkerContextSummary contextSummary, IEnumerable<WorkerToolCard> toolCards, WorkerConversationSessionState session)

    {

        var hints = new List<string>();

        foreach (var card in toolCards)

        {

            hints.AddRange(card.RecoveryHints);

        }



        if (session.PendingApprovalState != null && session.PendingApprovalState.HasPendingApproval)

        {

            hints.Add("Dang co approval pending. Neu token drift, preview lai truoc khi execute.");

        }



        if (string.Equals(contextSummary?.GroundingLevel, WorkerGroundingLevels.LiveContextOnly, StringComparison.OrdinalIgnoreCase))

        {

            hints.Add("Project context chua init. Chay project.init_preview -> project.init_apply de ground theo workspace.");

        }

        else if (string.Equals(contextSummary?.GroundingLevel, WorkerGroundingLevels.WorkspaceGrounded, StringComparison.OrdinalIgnoreCase))

        {

            hints.Add("Workspace da init nhung deep scan chua xong. Chay project.deep_scan de co evidence sau hon.");

        }



        hints.AddRange(contextSummary?.SuggestedNextTools?.Take(3).Select(x => "Next tool goi y: " + x) ?? Enumerable.Empty<string>());



        if (hints.Count == 0)

        {

            hints.Add("Neu can reset trang thai, bat dau bang worker.get_context hoac session.get_task_context.");

        }



        return hints

            .Where(x => !string.IsNullOrWhiteSpace(x))

            .Distinct(StringComparer.OrdinalIgnoreCase)

            .Take(6)

            .ToList();

    }



    private static string ResolveExecutionTier(IEnumerable<WorkerToolCard> toolCards, IEnumerable<WorkerActionCard> actionCards, WorkerPendingApprovalState pendingApproval)

    {

        if (toolCards.Any(x => string.Equals(x.ExecutionTier, WorkerExecutionTiers.Tier2, StringComparison.OrdinalIgnoreCase))

            || actionCards.Any(x => string.Equals(x.ExecutionTier, WorkerExecutionTiers.Tier2, StringComparison.OrdinalIgnoreCase))

            || (pendingApproval != null && pendingApproval.HasPendingApproval && string.Equals(InferExecutionTier(pendingApproval.ToolName), WorkerExecutionTiers.Tier2, StringComparison.OrdinalIgnoreCase)))

        {

            return WorkerExecutionTiers.Tier2;

        }



        if (toolCards.Any(x => string.Equals(x.ExecutionTier, WorkerExecutionTiers.Tier1, StringComparison.OrdinalIgnoreCase))

            || actionCards.Any(x => string.Equals(x.ExecutionTier, WorkerExecutionTiers.Tier1, StringComparison.OrdinalIgnoreCase)))

        {

            return WorkerExecutionTiers.Tier1;

        }



        return WorkerExecutionTiers.Tier0;

    }



    private static string BuildQueueSummary(QueueStateResponse queueState)

    {

        if (queueState == null)

        {

            return "Queue chua co du lieu.";

        }



        if (queueState.HasActiveInvocation)

        {

            return $"Dang chay {queueState.ActiveToolName} ({queueState.ActiveStage}) • pending {queueState.PendingCount}.";

        }



        return queueState.PendingCount > 0

            ? $"Queue dang cho {queueState.PendingCount} request(s) (high/normal/low={queueState.PendingHighPriorityCount}/{queueState.PendingNormalPriorityCount}/{queueState.PendingLowPriorityCount})."

            : "Queue dang ranh.";

    }



    private static string BuildWhyThisTool(string toolName)

    {

        if (toolName.StartsWith("context.", StringComparison.OrdinalIgnoreCase) || toolName.StartsWith("session.", StringComparison.OrdinalIgnoreCase))

        {

            return "Tool nay lay current context, queue, va delta de worker khong doan mo ho.";

        }



        if (toolName.StartsWith("review.", StringComparison.OrdinalIgnoreCase))

        {

            return "Tool nay uu tien read-only/QC de cho user thay model health truoc khi mutate.";

        }



        if (toolName.StartsWith("sheet.", StringComparison.OrdinalIgnoreCase) || toolName.StartsWith("family.", StringComparison.OrdinalIgnoreCase))

        {

            return "Tool nay tra ve evidence co cau truc de de review va recovery.";

        }



        if (toolName.StartsWith("audit.", StringComparison.OrdinalIgnoreCase) || toolName.StartsWith("workflow.", StringComparison.OrdinalIgnoreCase))

        {

            return "Tool nay di qua preview/approval pipeline va ton trong kernel Revit an toan.";

        }



        return "Tool nay phu hop voi intent hien tai va giu worker trong lane co kiem soat.";

    }



    private static List<string> BuildDefaultRecoveryHints(string toolName, bool succeeded)

    {

        var hints = new List<string>();

        if (!succeeded)

        {

            hints.Add("Doc diagnostics va context fingerprint truoc khi rerun.");

        }



        if (toolName.StartsWith("audit.", StringComparison.OrdinalIgnoreCase) || toolName.StartsWith("workflow.", StringComparison.OrdinalIgnoreCase))

        {

            hints.Add("Neu preview/token khong con hop le, preview lai de lay expected context moi.");

        }

        else

        {

            hints.Add("Neu ket qua chua du, chay them context.get_delta_summary hoac session.get_task_context.");

        }



        return hints.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    }



    private static string InferExecutionTier(string toolName)

    {

        if (string.Equals(toolName, ToolNames.ReviewModelHealth, StringComparison.OrdinalIgnoreCase)

            || string.Equals(toolName, ToolNames.ReviewSmartQc, StringComparison.OrdinalIgnoreCase)

            || string.Equals(toolName, ToolNames.ReviewSheetSummary, StringComparison.OrdinalIgnoreCase)

            || string.Equals(toolName, ToolNames.AuditNamingConvention, StringComparison.OrdinalIgnoreCase)

            || string.Equals(toolName, ToolNames.DataExportSchedule, StringComparison.OrdinalIgnoreCase))

        {

            return WorkerExecutionTiers.Tier0;

        }



        if (toolName.IndexOf("delete", StringComparison.OrdinalIgnoreCase) >= 0

            || toolName.IndexOf("purge", StringComparison.OrdinalIgnoreCase) >= 0

            || toolName.IndexOf("save", StringComparison.OrdinalIgnoreCase) >= 0

            || toolName.IndexOf("sync", StringComparison.OrdinalIgnoreCase) >= 0

            || toolName.IndexOf("export", StringComparison.OrdinalIgnoreCase) >= 0)

        {

            return WorkerExecutionTiers.Tier2;

        }



        if (toolName.StartsWith("audit.", StringComparison.OrdinalIgnoreCase)

            || toolName.StartsWith("playbook.execute::", StringComparison.OrdinalIgnoreCase)

            || toolName.StartsWith("element.", StringComparison.OrdinalIgnoreCase)

            || toolName.StartsWith("parameter.", StringComparison.OrdinalIgnoreCase)

            || toolName.StartsWith("sheet.", StringComparison.OrdinalIgnoreCase)

            || toolName.StartsWith("view.", StringComparison.OrdinalIgnoreCase)

            || toolName.StartsWith("workflow.", StringComparison.OrdinalIgnoreCase)

            || toolName.StartsWith("family.", StringComparison.OrdinalIgnoreCase))

        {

            return WorkerExecutionTiers.Tier1;

        }



        return WorkerExecutionTiers.Tier0;

    }



    private static List<WorkerContextPill> BuildContextPills(WorkerContextSummary contextSummary, WorkerConversationSessionState session, string executionTier)

    {

        contextSummary ??= new WorkerContextSummary();

        var pills = new List<WorkerContextPill>

        {

            new WorkerContextPill

            {

                Key = "document",

                Label = "Document",

                Value = string.IsNullOrWhiteSpace(contextSummary.DocumentTitle) ? "Chua co file" : contextSummary.DocumentTitle,

                Icon = "document",

                Tone = "neutral",

                Tooltip = contextSummary.DocumentKey,

                IsPrimary = true

            },

            new WorkerContextPill

            {

                Key = "view",

                Label = "View",

                Value = string.IsNullOrWhiteSpace(contextSummary.ActiveViewName) ? "Chua co view" : contextSummary.ActiveViewName,

                Icon = "view",

                Tone = "neutral",

                Tooltip = contextSummary.ActiveViewKey

            },

            new WorkerContextPill

            {

                Key = "selection",

                Label = "Selection",

                Value = contextSummary.SelectionCount == 1

                    ? "1 item"

                    : contextSummary.SelectionCount.ToString(System.Globalization.CultureInfo.InvariantCulture) + " items",

                Icon = "selection",

                Tone = contextSummary.SelectionCount > 0 ? "info" : "neutral",

                Tooltip = "Selection hien tai trong Revit"

            },

            new WorkerContextPill

            {

                Key = "workspace",

                Label = "Workspace",

                Value = string.IsNullOrWhiteSpace(contextSummary.WorkspaceId) ? session.Mission.WorkspaceId : contextSummary.WorkspaceId,

                Icon = "workspace",

                Tone = "accent",

                Tooltip = string.IsNullOrWhiteSpace(contextSummary.PackSummary) ? "Workspace mac dinh" : contextSummary.PackSummary

            },

            new WorkerContextPill

            {

                Key = "mission",

                Label = "Mission",

                Value = string.IsNullOrWhiteSpace(session.Mission.CapabilityDomain) ? CapabilityDomains.General : session.Mission.CapabilityDomain,

                Icon = "capability",

                Tone = "accent",

                Tooltip = session.Mission.PolicySummary

            },

            new WorkerContextPill

            {

                Key = "grounding",

                Label = "Grounding",

                Value = NormalizeGroundingLabel(contextSummary.GroundingLevel),

                Icon = "context",

                Tone = ResolveGroundingTone(contextSummary.GroundingLevel),

                Tooltip = FirstNonEmpty(contextSummary.GroundingSummary, contextSummary.ProjectSummary)

            },

            new WorkerContextPill

            {

                Key = "safety",

                Label = "Safety",

                Value = NormalizeTierLabel(executionTier),

                Icon = "safety",

                Tone = ResolveRiskTone(executionTier),

                Tooltip = contextSummary.QueueSummary

            }

        };



        return pills

            .Where(x => !string.IsNullOrWhiteSpace(x.Value))

            .ToList();

    }



    private static List<WorkerExecutionItem> BuildExecutionItems(IEnumerable<WorkerToolCard> toolCards, WorkerPendingApprovalState? pendingApproval)

    {

        var items = toolCards?

            .Select(card => new WorkerExecutionItem

            {

                ItemId = BuildStableItemId(card.ToolName, card.Stage, card.StatusCode, card.Summary, card.Progress),

                Title = string.IsNullOrWhiteSpace(card.ToolName) ? "Tool execution" : card.ToolName,

                Summary = card.Summary,

                Status = ResolveExecutionItemStatus(card),

                Stage = WorkerFlowStages.Normalize(card.Stage),

                ToolName = card.ToolName,

                Progress = card.Progress,

                TimestampUtc = DateTime.UtcNow,

                ExecutionTier = string.IsNullOrWhiteSpace(card.ExecutionTier) ? WorkerExecutionTiers.Tier0 : card.ExecutionTier,

                ArtifactRefs = card.ArtifactRefs?.ToList() ?? new List<string>()

            })

            .ToList() ?? new List<WorkerExecutionItem>();



        if (pendingApproval != null && pendingApproval.HasPendingApproval)

        {

            items.Insert(0, new WorkerExecutionItem

            {

                ItemId = pendingApproval.PendingActionId,

                Title = string.IsNullOrWhiteSpace(pendingApproval.ToolName) ? "Pending approval" : pendingApproval.ToolName,

                Summary = string.IsNullOrWhiteSpace(pendingApproval.Summary) ? "Dang cho anh phe duyet." : pendingApproval.Summary,

                Status = WorkerExecutionItemStates.AwaitingApproval,

                Stage = WorkerFlowStages.Approval,

                ToolName = pendingApproval.ToolName,

                TimestampUtc = pendingApproval.ExpiresUtc ?? DateTime.UtcNow,

                ExecutionTier = ResolvePendingExecutionTier(pendingApproval)

            });

        }



        return items;

    }



    private static List<WorkerEvidenceItem> BuildEvidenceItems(IEnumerable<string> artifactRefs, IEnumerable<WorkerToolCard> toolCards, CompiledTaskPlan compiledPlan)

    {

        var evidence = new List<WorkerEvidenceItem>();

        var refs = artifactRefs?.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList()

            ?? new List<string>();

        foreach (var artifactRef in refs)

        {

            var source = toolCards?.FirstOrDefault(x => x.ArtifactRefs.Any(a => string.Equals(a, artifactRef, StringComparison.OrdinalIgnoreCase)));

            var verified = source?.Succeeded == true

                           && string.Equals(WorkerFlowStages.Normalize(source.Stage), WorkerFlowStages.Verify, StringComparison.OrdinalIgnoreCase);

            evidence.Add(new WorkerEvidenceItem

            {

                ArtifactRef = artifactRef,

                Title = artifactRef.Split('/', '\\').LastOrDefault() ?? artifactRef,

                Summary = source?.Summary ?? "Artifact san sang de review/zoom/export.",

                Status = source?.Succeeded == false

                    ? WorkerExecutionItemStates.Failed

                    : verified

                        ? WorkerExecutionItemStates.Verified

                        : WorkerExecutionItemStates.Completed,

                SourceToolName = source?.ToolName ?? string.Empty,

                VerificationMode = compiledPlan?.VerificationMode ?? ToolVerificationModes.ReportOnly,

                Verified = verified

            });

        }



        if (evidence.Count == 0 && toolCards != null)

        {

            foreach (var card in toolCards.Where(x => !string.IsNullOrWhiteSpace(x.Summary)).Take(3))

            {

                evidence.Add(new WorkerEvidenceItem

                {

                    ArtifactRef = card.ToolName,

                    Title = card.ToolName,

                    Summary = card.Summary,

                    Status = card.Succeeded ? WorkerExecutionItemStates.Completed : WorkerExecutionItemStates.Failed,

                    SourceToolName = card.ToolName,

                    VerificationMode = compiledPlan?.VerificationMode ?? ToolVerificationModes.ReportOnly,

                    Verified = false

                });

            }

        }



        return evidence;

    }



    private static List<WorkerCommandSuggestion> BuildSuggestedCommands(IEnumerable<WorkerActionCard> actionCards, WorkerContextSummary contextSummary)

    {

        var suggestions = new List<WorkerCommandSuggestion>();



        foreach (var action in actionCards ?? Enumerable.Empty<WorkerActionCard>())

        {

            suggestions.Add(new WorkerCommandSuggestion

            {

                CommandId = string.IsNullOrWhiteSpace(action.ToolName) ? action.ActionId : action.ToolName,

                Label = string.IsNullOrWhiteSpace(action.Title) ? action.ActionKind : action.Title,

                Summary = action.Summary,

                ToolName = action.ToolName,

                RequiresApproval = action.RequiresApproval,

                IsPrimary = action.IsPrimary,

                SurfaceId = action.RequiresApproval ? WorkerSurfaceIds.Assistant : WorkerSurfaceIds.Commands

            });

        }



        foreach (var tool in contextSummary?.SuggestedNextTools?.Where(x => !string.IsNullOrWhiteSpace(x)).Take(4) ?? Enumerable.Empty<string>())

        {

            if (!CommandAtlasService.IsMvpCuratedCommand(tool))

            {

                continue;

            }



            if (suggestions.Any(x => string.Equals(x.ToolName, tool, StringComparison.OrdinalIgnoreCase) || string.Equals(x.CommandId, tool, StringComparison.OrdinalIgnoreCase)))

            {

                continue;

            }



            suggestions.Add(new WorkerCommandSuggestion

            {

                CommandId = tool,

                Label = tool,

                Summary = "Goi y next step theo context hien tai.",

                ToolName = tool,

                SurfaceId = WorkerSurfaceIds.Commands

            });

        }



        return suggestions.Take(6).ToList();

    }



    private static WorkerRiskSummary BuildRiskSummary(

        WorkerPendingApprovalState? pendingApproval,

        string executionTier,

        IEnumerable<WorkerToolCard> toolCards,

        CompiledTaskPlan compiledPlan,

        IEnumerable<string> artifactRefs)

    {

        var verificationMode = compiledPlan?.VerificationMode ?? ToolVerificationModes.ReportOnly;

        var affectedCount = artifactRefs?.Count() ?? 0;



        if (pendingApproval != null && pendingApproval.HasPendingApproval)

        {

            var pendingTier = ResolvePendingExecutionTier(pendingApproval);

            return new WorkerRiskSummary

            {

                RiskLevel = string.Equals(pendingTier, WorkerExecutionTiers.Tier2, StringComparison.OrdinalIgnoreCase)

                    ? WorkerRiskLevels.High

                    : WorkerRiskLevels.Moderate,

                Label = "Approval required",

                Summary = string.IsNullOrWhiteSpace(pendingApproval.Summary)

                    ? "Co thay doi dang cho anh phe duyet truoc khi execute."

                    : pendingApproval.Summary,

                RequiresApproval = true,

                VerificationMode = verificationMode,

                AffectedElementCount = affectedCount,

                ExecutionTier = pendingTier

            };

        }



        if (string.Equals(executionTier, WorkerExecutionTiers.Tier2, StringComparison.OrdinalIgnoreCase))

        {

            return new WorkerRiskSummary

            {

                RiskLevel = WorkerRiskLevels.High,

                Label = "High-risk mutation",

                Summary = "Lane nay can preview + verify chat che.",

                VerificationMode = verificationMode,

                AffectedElementCount = affectedCount,

                ExecutionTier = executionTier

            };

        }



        if (string.Equals(executionTier, WorkerExecutionTiers.Tier1, StringComparison.OrdinalIgnoreCase))

        {

            return new WorkerRiskSummary

            {

                RiskLevel = WorkerRiskLevels.Low,

                Label = "Deterministic mutation",

                Summary = "Co thay doi model nhung van nam trong lane policy-backed.",

                VerificationMode = verificationMode,

                AffectedElementCount = affectedCount,

                ExecutionTier = executionTier

            };

        }



        var hasFailure = toolCards != null && toolCards.Any(x => !x.Succeeded);

        return new WorkerRiskSummary

        {

            RiskLevel = hasFailure ? WorkerRiskLevels.Moderate : WorkerRiskLevels.ReadOnly,

            Label = hasFailure ? "Needs review" : "Read-only / safe",

            Summary = hasFailure

                ? "Co tool call chua thanh cong; xem execution rail va recovery hints."

                : "Session nay dang o lane read-only hoac preview-safe.",

            VerificationMode = verificationMode,

            AffectedElementCount = affectedCount,

            ExecutionTier = executionTier

        };

    }



    private static WorkerSurfaceHint ResolveSurfaceHint(

        WorkerConversationSessionState session,

        IEnumerable<string> artifactRefs,

        IEnumerable<WorkerToolCard> toolCards,

        IEnumerable<WorkerActionCard> actionCards)

    {

        if (session.PendingApprovalState != null && session.PendingApprovalState.HasPendingApproval)

        {

            return new WorkerSurfaceHint

            {

                SurfaceId = WorkerSurfaceIds.Assistant,

                Reason = "Dang co approval pending.",

                Emphasis = "approval"

            };

        }



        if (artifactRefs != null && artifactRefs.Any())

        {

            return new WorkerSurfaceHint

            {

                SurfaceId = WorkerSurfaceIds.Evidence,

                Reason = "Run hien tai da tao artifact/evidence.",

                Emphasis = "evidence"

            };

        }



        if (toolCards != null && toolCards.Any())

        {

            return new WorkerSurfaceHint

            {

                SurfaceId = WorkerSurfaceIds.Assistant,

                Reason = "Dang co live execution rail va tool feedback.",

                Emphasis = "execution"

            };

        }



        if (actionCards != null && actionCards.Any())

        {

            return new WorkerSurfaceHint

            {

                SurfaceId = WorkerSurfaceIds.Commands,

                Reason = "Worker dang goi y quick commands / next actions.",

                Emphasis = "commands"

            };

        }



        return new WorkerSurfaceHint

        {

            SurfaceId = WorkerSurfaceIds.Activity,

            Reason = "Khong co mutation dang cho; xem lich su va session timeline.",

            Emphasis = "history"

        };

    }



    private static string ResolveExecutionItemStatus(WorkerToolCard card)

    {

        if (card == null)

        {

            return WorkerExecutionItemStates.Planned;

        }



        if (!card.Succeeded)

        {

            return WorkerExecutionItemStates.Failed;

        }



        var normalizedStage = WorkerFlowStages.Normalize(card.Stage);

        if (string.Equals(normalizedStage, WorkerFlowStages.Verify, StringComparison.OrdinalIgnoreCase))

        {

            return WorkerExecutionItemStates.Verified;

        }



        if (string.Equals(normalizedStage, WorkerFlowStages.Run, StringComparison.OrdinalIgnoreCase)

            && card.Progress > 0 && card.Progress < 100)

        {

            return WorkerExecutionItemStates.Running;

        }



        return WorkerExecutionItemStates.Completed;

    }



    private static string NormalizeTierLabel(string executionTier)

    {

        if (string.Equals(executionTier, WorkerExecutionTiers.Tier2, StringComparison.OrdinalIgnoreCase))

        {

            return "Approval / destructive";

        }



        if (string.Equals(executionTier, WorkerExecutionTiers.Tier1, StringComparison.OrdinalIgnoreCase))

        {

            return "Deterministic mutate";

        }



        return "Read-only";

    }



    private static string NormalizeGroundingLabel(string? groundingLevel)

    {

        if (string.Equals(groundingLevel, WorkerGroundingLevels.DeepScanGrounded, StringComparison.OrdinalIgnoreCase))

        {

            return "Deep scan grounded";

        }



        if (string.Equals(groundingLevel, WorkerGroundingLevels.WorkspaceGrounded, StringComparison.OrdinalIgnoreCase))

        {

            return "Workspace grounded";

        }



        return "Live context only";

    }



    private static string BuildStableItemId(string toolName, string stage, string statusCode, string summary, double progress)

    {

        var normalizedTool = string.IsNullOrWhiteSpace(toolName) ? "tool" : toolName.Trim();

        var normalizedStage = WorkerFlowStages.Normalize(string.IsNullOrWhiteSpace(stage) ? WorkerFlowStages.Thinking : stage.Trim());

        var normalizedStatus = string.IsNullOrWhiteSpace(statusCode) ? "status" : statusCode.Trim();

        var normalizedSummary = string.IsNullOrWhiteSpace(summary) ? string.Empty : summary.Trim();

        return $"{normalizedTool}|{normalizedStage}|{normalizedStatus}|{Math.Round(progress, 2).ToString(System.Globalization.CultureInfo.InvariantCulture)}|{ComputeStableHash(normalizedSummary):X8}";

    }



    private static int ComputeStableHash(string value)

    {

        unchecked

        {

            var hash = 23;

            foreach (var ch in value ?? string.Empty)

            {

                hash = (hash * 31) + ch;

            }



            return hash;

        }

    }



    private static string ResolvePendingExecutionTier(WorkerPendingApprovalState? pendingApproval)

    {

        if (pendingApproval == null)

        {

            return WorkerExecutionTiers.Tier0;

        }



        if (string.IsNullOrWhiteSpace(pendingApproval.ExecutionTier)

            || string.Equals(pendingApproval.ExecutionTier, WorkerExecutionTiers.Tier0, StringComparison.OrdinalIgnoreCase))

        {

            return InferExecutionTier(pendingApproval.ToolName);

        }



        return pendingApproval.ExecutionTier;

    }



    private static string ResolveRiskTone(string executionTier)

    {

        if (string.Equals(executionTier, WorkerExecutionTiers.Tier2, StringComparison.OrdinalIgnoreCase))

        {

            return "danger";

        }



        if (string.Equals(executionTier, WorkerExecutionTiers.Tier1, StringComparison.OrdinalIgnoreCase))

        {

            return "warning";

        }



        return "success";

    }



    private static string ResolveGroundingTone(string? groundingLevel)

    {

        if (string.Equals(groundingLevel, WorkerGroundingLevels.DeepScanGrounded, StringComparison.OrdinalIgnoreCase))

        {

            return "success";

        }



        if (string.Equals(groundingLevel, WorkerGroundingLevels.WorkspaceGrounded, StringComparison.OrdinalIgnoreCase))

        {

            return "info";

        }



        return "warning";

    }



    private QueueStateResponse GetQueueState()

    {

        return AgentHost.TryGetCurrent(out var runtime) && runtime != null

            ? runtime.ExternalEventHandler.GetQueueState(runtime.Queue)

            : new QueueStateResponse();

    }



    private List<ToolManifest> GetToolCatalog()

    {

        return AgentHost.TryGetCurrent(out var runtime) && runtime != null

            ? runtime.Registry.GetToolCatalog()

            : new List<ToolManifest>();

    }



    private List<WorkerToolCard> NormalizeToolCards(IEnumerable<WorkerToolCard> toolCards)

    {

        return (toolCards ?? Enumerable.Empty<WorkerToolCard>())

            .Select(card =>

            {

                card.Stage = WorkerFlowStages.Normalize(card.Stage);

                return card;

            })

            .ToList();

    }



    private OnboardingStatusDto BuildOnboardingStatus(WorkerConversationSessionState session, WorkerContextSummary contextSummary, PendingApprovalRef? pendingApproval)

    {

        var workspaceId = string.IsNullOrWhiteSpace(contextSummary?.WorkspaceId)

            ? session?.Mission?.WorkspaceId ?? string.Empty

            : contextSummary?.WorkspaceId ?? string.Empty;

        var safePendingApproval = pendingApproval ?? new PendingApprovalRef();

        var bundle = string.IsNullOrWhiteSpace(workspaceId)

            ? new ProjectContextBundleResponse

            {

                WorkspaceId = workspaceId,

                StatusCode = StatusCodes.ProjectContextNotInitialized,

                Exists = false

            }

            : _copilotTasks.GetProjectContextBundle(GetToolCatalog(), new ProjectContextBundleRequest

            {

                WorkspaceId = workspaceId,

                Query = string.Empty,

                MaxSourceRefs = 3,

                MaxStandardsRefs = 3

            });

        var initStatus = bundle.Exists

            ? ProjectOnboardingStatuses.Initialized

            : ProjectOnboardingStatuses.NotInitialized;

        var deepScanStatus = bundle.Exists && !string.IsNullOrWhiteSpace(bundle.DeepScanStatus)

            ? bundle.DeepScanStatus

            : ProjectDeepScanStatuses.NotStarted;

        var primaryModelStatus = bundle.Exists && !string.IsNullOrWhiteSpace(bundle.PrimaryModelStatus)

            ? bundle.PrimaryModelStatus

            : contextSummary?.ProjectPrimaryModelStatus ?? ProjectPrimaryModelStatuses.NotRequested;



        return new OnboardingStatusDto

        {

            WorkspaceId = bundle.WorkspaceId ?? workspaceId,

            WorkspaceRootPath = bundle.WorkspaceRootPath ?? string.Empty,

            InitStatus = initStatus,

            DeepScanStatus = deepScanStatus,

            ResumeEligible = session != null

                && !string.Equals(session.Status, WorkerSessionStates.Ended, StringComparison.OrdinalIgnoreCase)

                && (!string.IsNullOrWhiteSpace(safePendingApproval.PendingActionId)

                    || (session.Messages?.Count ?? 0) > 0

                    || !string.IsNullOrWhiteSpace(session.Mission?.MissionId)),

            SessionId = session?.SessionId ?? string.Empty,

            MissionId = session?.Mission?.MissionId ?? string.Empty,

            PendingApproval = safePendingApproval,

            PrimaryModelStatus = primaryModelStatus,

            Summary = BuildOnboardingSummary(bundle, safePendingApproval)

        };

    }



    private static string BuildOnboardingSummary(ProjectContextBundleResponse bundle, PendingApprovalRef? pendingApproval)

    {

        if (pendingApproval != null && !string.IsNullOrWhiteSpace(pendingApproval.PendingActionId))

        {

            return string.IsNullOrWhiteSpace(pendingApproval.Summary)

                ? "Đang có preview pending cần xử lý trước khi tiếp tục."

                : pendingApproval.Summary;

        }



        if (bundle == null || !bundle.Exists)

        {

            return "Chat vẫn sẵn sàng, nhưng project context chưa được khởi tạo. Dùng Init workspace để gắn context cho model này.";

        }



        if (string.IsNullOrWhiteSpace(bundle.DeepScanStatus)

            || string.Equals(bundle.DeepScanStatus, ProjectDeepScanStatuses.NotStarted, StringComparison.OrdinalIgnoreCase))

        {

            return "Workspace đã có context cơ bản. Dùng deep scan khi anh cần pattern và evidence sâu hơn.";

        }



        return string.IsNullOrWhiteSpace(bundle.Summary)

            ? "Workspace đã sẵn sàng để tiếp tục."

            : bundle.Summary;

    }



    private static void ApplyPlannerHints(WorkerMessageRequest request, WorkerDecision decision, WorkerContextSummary contextSummary)

    {

        if (request == null || decision == null || contextSummary == null)

        {

            return;

        }



        var plannerTools = request.ChosenToolSequence?

            .Where(x => !string.IsNullOrWhiteSpace(x))

            .Distinct(StringComparer.OrdinalIgnoreCase)

            .ToList() ?? new List<string>();

        if (plannerTools.Count > 0)

        {

            decision.PlannedTools = plannerTools

                .Concat(decision.PlannedTools ?? new List<string>())

                .Where(x => !string.IsNullOrWhiteSpace(x))

                .Distinct(StringComparer.OrdinalIgnoreCase)

                .ToList();

            contextSummary.SuggestedNextTools = decision.PlannedTools.Take(8).ToList();

        }



        if (!string.IsNullOrWhiteSpace(request.PlanningSummary))

        {

            decision.PlanSummary = request.PlanningSummary.Trim();

        }



        if (!string.IsNullOrWhiteSpace(request.PlannerTraceSummary))

        {

            decision.ReasoningSummary = string.IsNullOrWhiteSpace(decision.ReasoningSummary)

                ? request.PlannerTraceSummary.Trim()

                : decision.ReasoningSummary + " | " + request.PlannerTraceSummary.Trim();

        }



        if (!string.IsNullOrWhiteSpace(request.GroundingLevel))

        {

            contextSummary.GroundingLevel = request.GroundingLevel.Trim();

        }



        if (request.EvidenceRefs != null && request.EvidenceRefs.Count > 0)

        {

            contextSummary.GroundingRefs = contextSummary.GroundingRefs

                .Concat(request.EvidenceRefs)

                .Where(x => !string.IsNullOrWhiteSpace(x))

                .Distinct(StringComparer.OrdinalIgnoreCase)

                .ToList();

            contextSummary.GroundingSummary = string.IsNullOrWhiteSpace(contextSummary.GroundingSummary)

                ? $"Planner hints: {string.Join(", ", contextSummary.GroundingRefs.Take(4))}"

                : contextSummary.GroundingSummary;

        }

    }



    private static void AddPlannerHintActionCards(List<WorkerActionCard> actionCards, WorkerMessageRequest request, WorkerDecision decision)

    {

        if (actionCards == null || request == null)

        {

            return;

        }



        var plannedTools = request.ChosenToolSequence?

            .Where(x => !string.IsNullOrWhiteSpace(x))

            .Distinct(StringComparer.OrdinalIgnoreCase)

            .ToList() ?? new List<string>();

        if (plannedTools.Count == 0)

        {

            return;

        }



        actionCards.Insert(0, new WorkerActionCard

        {

            ActionKind = WorkerActionKinds.Context,

            Title = "Agent plan",

            Summary = FirstNonEmpty(request.PlanningSummary, decision?.PlanSummary, string.Join(" → ", plannedTools.Take(4))),

            ToolName = plannedTools[0],

            IsPrimary = actionCards.Count == 0,

            RequiresApproval = false,

            ExecutionTier = WorkerExecutionTiers.Tier0,

            WhyThisAction = FirstNonEmpty(request.PlannerTraceSummary, "Planner da chon tool chain uu tien cho mission nay."),

            Confidence = 0.88d,

            RecoveryHint = "Neu plan chua dung y, noi ro muc tieu hoac y muon script/tool can tao.",

            AutoExecutionEligible = true

        });

    }

}

