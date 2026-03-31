using System;
using System.IO;
using System.Net.Http;
using Autodesk.Revit.UI;
using BIM765T.Revit.Agent.Config;
using BIM765T.Revit.Agent.Infrastructure.Bridge;
using BIM765T.Revit.Agent.Infrastructure.Failures;
using BIM765T.Revit.Agent.Infrastructure.Logging;
using BIM765T.Revit.Agent.Infrastructure.Observability;
using BIM765T.Revit.Agent.Infrastructure.Time;
using BIM765T.Revit.Agent.Services.Bridge;
using BIM765T.Revit.Agent.Services.Context;
using BIM765T.Revit.Agent.Services.Hull;
using BIM765T.Revit.Agent.Services.Platform;
using BIM765T.Revit.Agent.Services.Review;
using BIM765T.Revit.Agent.Workflow;
using BIM765T.Revit.Copilot.Core.Brain;
using BIM765T.Revit.Copilot.Core.Memory;
using BIM765T.Revit.Copilot.Core;

namespace BIM765T.Revit.Agent.Infrastructure;

internal static class AgentHost
{
    internal static readonly System.Guid DockPaneGuid = new System.Guid("6E4E4419-1BD4-4F13-9915-C11FF587F6FA");
    private const int NarrationMaxTokens = 256;
    private const int PlannerMaxTokens = 640;
    private static readonly LlmTimeoutProfile AgentTimeoutProfile = LlmTimeoutProfile.Default;

    private static UIControlledApplication? _uiControlledApp;
    private static IAgentHostServices? _current;

    internal static IAgentHostServices Current =>
        _current ?? throw new System.InvalidOperationException("Agent host is not initialized.");

    internal static bool TryGetCurrent(out IAgentHostServices? runtime)
    {
        runtime = _current;
        return runtime != null;
    }

    internal static UI.AgentPaneControl? PaneControl { get; set; }

    internal static void Initialize(UIControlledApplication app)
    {
        _uiControlledApp = app;
        PaneControl = null;

        var clock = new SystemClock();
        var settingsResult = SettingsLoader.Load();
        var settings = settingsResult.Settings;
        var policyResult = PolicyLoader.Load();
        var policy = policyResult.Policy;
        var logger = new FileLogger(settings.VerboseLogging, settings.JsonLogFormat);

        if (settingsResult.HasLoadError)
        {
            logger.Error($"[CONFIG] settings.json load failed - using defaults. Error: {settingsResult.LoadError}");
        }

        if (policyResult.HasLoadError)
        {
            logger.Error($"[CONFIG] policy.json load failed - using defaults. Error: {policyResult.LoadError}");
        }

        var currentContextService = new CurrentContextService();
        var hullCollector = new HullSourceCollector();
        var hullPlanner = new HullPlanner();
        var hullValidator = new HullValidationService();
        var reviewRuleEngine = new ReviewRuleEngineService();

        var cache = new DocumentCacheService(ResolveDocumentKey);
        cache.Attach(app);

        var typeCatalog = new TypeCatalogService(cache);
        var familyAxisAudit = new FamilyAxisAuditService();
        var penetrationShadow = new PenetrationShadowService();
        var journal = new OperationJournalService(logger, settings.MaxRecentOperations, settings.EnableOperationJournal, clock);
        var dialogGuard = new AutomationDialogGuard(logger);
        dialogGuard.Attach(app);

        var eventIndex = new EventIndexService(
            logger,
            settings.MaxRecentEvents,
            ResolveDocumentKey,
            view => $"view:{view.Id.Value}",
            clock);
        if (settings.EnableEventIndex)
        {
            eventIndex.Attach(app);
        }

        var documentResolver = new DocumentResolverService(cache);
        var approvalGate = new ApprovalService(settings, logger, clock);
        var snapshotService = new SnapshotService();
        var platform = new PlatformServices(settings, policy, logger, currentContextService, journal, eventIndex, documentResolver, approvalGate, snapshotService, clock);
        var sheetView = new SheetViewManagementService();
        var dataExport = new DataExportService();
        var scheduleExtraction = new ScheduleExtractionService();
        var familyXray = new FamilyXrayService();
        var sheetIntelligence = new SheetIntelligenceService(scheduleExtraction);
        var audit = new AuditService();
        var mutation = new MutationService();
        var templateSheetAnalysis = new TemplateSheetAnalysisService();
        var fixLoop = new FixLoopService(platform, mutation, audit, sheetView, templateSheetAnalysis);
        var deliveryOps = new DeliveryOpsService(platform);
        var modelStateGraph = new ModelStateGraphService(cache);
        var copilotStatePaths = new CopilotStatePaths();
        var copilotStore = new CopilotTaskRunStore(copilotStatePaths);
        var taskMetrics = new TaskMetricsService();
        var contextAnchors = new ContextAnchorService();
        var artifactSummary = new ArtifactSummaryService();
        var toolSearch = new ToolCapabilitySearchService();
        var packCatalog = new PackCatalogService(AppDomain.CurrentDomain.BaseDirectory);
        var workspaceCatalog = new WorkspaceCatalogService(AppDomain.CurrentDomain.BaseDirectory);
        var standardsCatalog = new StandardsCatalogService(packCatalog, workspaceCatalog);
        var playbookLoader = new PlaybookLoaderService(PlaybookLoaderService.LoadAll(AppDomain.CurrentDomain.BaseDirectory));
        var playbookOrchestration = new PlaybookOrchestrationService(playbookLoader, packCatalog, workspaceCatalog, standardsCatalog);
        var projectInit = new ProjectInitService(packCatalog, workspaceCatalog, AppDomain.CurrentDomain.BaseDirectory);
        var projectContextComposer = new ProjectContextComposer(projectInit, workspaceCatalog, standardsCatalog, playbookOrchestration, AppDomain.CurrentDomain.BaseDirectory);
        var projectDeepScan = new ProjectDeepScanService(projectInit, AppDomain.CurrentDomain.BaseDirectory);
        var toolGraphOverlay = ToolGraphOverlayService.LoadDefault();
        var toolGuidance = new ToolGuidanceService(toolSearch, toolGraphOverlay, playbookLoader);
        var contextDelta = new ContextDeltaSummaryService();
        var copilotLogger = new AgentLoggerCopilotAdapter(logger);
        var smartQcAggregation = new SmartQcAggregationService(copilotLogger);
        var taskRecovery = new TaskRecoveryPlanner();
        var workflowRuntime = new WorkflowRuntimeService(platform, reviewRuleEngine, familyAxisAudit, penetrationShadow, mutation, dataExport, sheetView);
        var copilotTasks = new CopilotTaskService(platform, workflowRuntime, fixLoop, modelStateGraph, copilotStore, taskMetrics, contextAnchors, artifactSummary, toolSearch, toolGuidance, contextDelta, taskRecovery, packCatalog: packCatalog, workspaceCatalog: workspaceCatalog, standardsCatalog: standardsCatalog, playbookOrchestration: playbookOrchestration, projectInit: projectInit, projectContextComposer: projectContextComposer, projectDeepScan: projectDeepScan);
        var smartQc = new SmartQcService(platform, reviewRuleEngine, audit, smartQcAggregation);
        var queryPerformance = new QueryPerformanceService();
        var spatialIntelligence = new SpatialIntelligenceService();
        var personasDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "personas");
        var personas = new PersonaRegistry(personasDirectory);
        var conversations = new ConversationManager();
        var missions = new MissionCoordinator();
        var intentClassifier = new IntentClassifier();
        var secretProvider = new EnvSecretProvider();
        var llmConfigResolver = new OpenRouterFirstLlmProviderConfigResolver(secretProvider);
        var llmProfile = llmConfigResolver.Resolve();
        var sharedHttpClient = new HttpClient();
        var llmClient = CreateNarrationClient(llmProfile, sharedHttpClient, copilotLogger);
        var planner = CreatePlanner(llmProfile, sharedHttpClient, copilotLogger);
        var responseEnhancer = new LlmResponseEnhancer(llmClient, copilotLogger, AgentTimeoutProfile);
        var reasoning = new WorkerReasoningEngine(intentClassifier, personas, responseEnhancer, planner);
        var sessionMemory = new SessionMemoryStore();
        var episodicMemory = new EpisodicMemoryStore(copilotStatePaths);
        var workerPlaybookExecution = new WorkerPlaybookExecutionService(platform, playbookLoader, clock);
        var worker = new WorkerService(platform, audit, smartQc, scheduleExtraction, familyXray, sheetIntelligence, copilotTasks, conversations, missions, reasoning, personas, sessionMemory, episodicMemory, workerPlaybookExecution, clock, responseEnhancer);
        var viewAutomation = new ViewAutomationService();
        var fileLifecycle = new FileLifecycleService();
        var platformBundle = new PlatformBundle(platform, mutation, viewAutomation, fileLifecycle);
        var inspectionBundle = new InspectionBundle(reviewRuleEngine, typeCatalog, familyAxisAudit, penetrationShadow, audit, smartQc, familyXray, sheetIntelligence, scheduleExtraction, queryPerformance, spatialIntelligence);
        var hullBundle = new HullBundle(hullCollector, hullPlanner, hullValidator);
        var workflowBundle = new WorkflowBundle(workflowRuntime, fixLoop, deliveryOps, templateSheetAnalysis, sheetView, dataExport);
        var copilotBundle = new CopilotBundle(copilotTasks, worker);
        var registry = new ToolRegistry(platformBundle, inspectionBundle, hullBundle, workflowBundle, copilotBundle);
        var queue = new ToolInvocationQueue();
        var toolExecutor = new ToolExecutor(logger, platform, registry, dialogGuard, clock);
        var externalEventHandler = new ToolExternalEventHandler(queue, toolExecutor, registry, logger, clock);
        var externalEvent = ExternalEvent.Create(externalEventHandler);
        externalEventHandler.SetSelfEvent(externalEvent);
        var pipeScheduler = new ExternalEventPipeRequestScheduler(queue, externalEvent, logger);
        var callerAuthorizer = new WindowsPipeCallerAuthorizer();
        var kernelPipeServer = new KernelPipeHostedService(settings, logger, pipeScheduler, callerAuthorizer);
        kernelPipeServer.Start();

        _current = new AgentHostRuntime(
            settings,
            policy,
            logger,
            queue,
            externalEventHandler,
            externalEvent,
            kernelPipeServer,
            toolExecutor,
            platform,
            cache,
            journal,
            eventIndex,
            dialogGuard,
            workflowRuntime,
            copilotTasks,
            registry);

        logger.Info("Agent host initialized.");
    }

    internal static void Shutdown()
    {
        var runtime = _current;
        var logger = runtime?.Logger;

        if (runtime == null)
        {
            PaneControl = null;
            _uiControlledApp = null;
            return;
        }

        TryShutdown("KernelPipeServer", runtime.KernelPipeServer.Dispose, logger);
        TryShutdown("ApprovalStoreFlush", runtime.Platform.Approval.FlushPendingPersist, logger);
        TryShutdown("EventIndex", runtime.EventIndex.Detach, logger);
        TryShutdown("DocumentCache", runtime.Cache.Detach, logger);
        TryShutdown("DialogGuard", () =>
        {
            if (_uiControlledApp != null)
            {
                runtime.DialogGuard.Detach(_uiControlledApp);
            }
        }, logger);
        TryShutdown("ExternalEvent", runtime.ExternalEvent.Dispose, logger);

        _current = null;
        PaneControl = null;
        _uiControlledApp = null;
        logger?.Info("Agent host shutdown.");
    }

    private static void TryShutdown(string name, System.Action action, IAgentLogger? logger)
    {
        try
        {
            action();
        }
        catch (System.Exception ex)
        {
            logger?.Error($"AgentHost shutdown step failed: {name}.", ex);
        }
    }

    private static string ResolveDocumentKey(Autodesk.Revit.DB.Document doc)
    {
        var path = doc.PathName ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(path))
            return "path:" + path.Trim().ToLowerInvariant();
        try
        {
            var title = doc.Title;
            return "title:" + (title ?? string.Empty).Trim().ToLowerInvariant();
        }
        catch
        {
            return "title:unknown";
        }
    }

    private static ILlmClient CreateNarrationClient(LlmProviderConfiguration profile, HttpClient httpClient, ICopilotLogger? logger = null)
    {
        if (profile == null || !profile.IsConfigured)
        {
            return new NullLlmClient(logger);
        }

        if (string.Equals(profile.ProviderKind, "anthropic", StringComparison.OrdinalIgnoreCase))
        {
            return new AnthropicLlmClient(
                httpClient,
                profile.ApiKey,
                model: profile.ResponseModel,
                apiUrl: profile.ApiUrl,
                logger: logger,
                timeoutProfile: AgentTimeoutProfile);
        }

        return new OpenAiCompatibleLlmClient(
            httpClient,
            profile.ApiKey,
            model: profile.ResponseModel,
            maxTokens: NarrationMaxTokens,
            apiUrl: profile.ApiUrl,
            providerLabel: profile.ConfiguredProvider,
            organization: profile.Organization,
            project: profile.Project,
            httpReferer: profile.HttpReferer,
            xTitle: profile.XTitle,
            logger: logger,
            timeoutProfile: AgentTimeoutProfile);
    }

    private static ILlmPlanner CreatePlanner(LlmProviderConfiguration profile, HttpClient httpClient, ICopilotLogger? logger = null)
    {
        if (profile == null
            || !profile.IsConfigured
            || !string.Equals(profile.ProviderKind, "openai_compatible", StringComparison.OrdinalIgnoreCase))
        {
            return new NullLlmPlanner();
        }

        var primary = new OpenAiCompatibleLlmClient(
            httpClient,
            profile.ApiKey,
            model: profile.PlannerPrimaryModel,
            maxTokens: PlannerMaxTokens,
            apiUrl: profile.ApiUrl,
            providerLabel: profile.ConfiguredProvider,
            organization: profile.Organization,
            project: profile.Project,
            httpReferer: profile.HttpReferer,
            xTitle: profile.XTitle,
            logger: logger,
            timeoutProfile: AgentTimeoutProfile);
        OpenAiCompatibleLlmClient? fallback = null;
        if (!string.IsNullOrWhiteSpace(profile.PlannerFallbackModel)
            && !string.Equals(profile.PlannerFallbackModel, profile.PlannerPrimaryModel, StringComparison.OrdinalIgnoreCase))
        {
            fallback = new OpenAiCompatibleLlmClient(
                httpClient,
                profile.ApiKey,
                model: profile.PlannerFallbackModel,
                maxTokens: PlannerMaxTokens,
                apiUrl: profile.ApiUrl,
                providerLabel: profile.ConfiguredProvider,
                organization: profile.Organization,
                project: profile.Project,
                httpReferer: profile.HttpReferer,
                xTitle: profile.XTitle,
                logger: logger,
                timeoutProfile: AgentTimeoutProfile);
        }

        return new LlmPlanningService(profile, primary, fallback, AgentTimeoutProfile);
    }
}
