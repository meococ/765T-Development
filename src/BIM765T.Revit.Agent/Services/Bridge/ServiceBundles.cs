using BIM765T.Revit.Agent.Services.Hull;
using BIM765T.Revit.Agent.Services.Platform;
using BIM765T.Revit.Agent.Services.Review;
using BIM765T.Revit.Agent.Workflow;

namespace BIM765T.Revit.Agent.Services.Bridge;

/// <summary>
/// Groups core platform + mutation + approval services.
/// </summary>
internal sealed class PlatformBundle
{
    internal PlatformBundle(
        PlatformServices platform,
        MutationService mutation,
        ViewAutomationService viewAutomation,
        FileLifecycleService fileLifecycle)
    {
        Platform = platform;
        Mutation = mutation;
        ViewAutomation = viewAutomation;
        FileLifecycle = fileLifecycle;
    }

    internal PlatformServices Platform { get; }
    internal MutationService Mutation { get; }
    internal ViewAutomationService ViewAutomation { get; }
    internal FileLifecycleService FileLifecycle { get; }
}

/// <summary>
/// Groups review, audit, QC, and inspection services.
/// </summary>
internal sealed class InspectionBundle
{
    internal InspectionBundle(
        ReviewRuleEngineService reviewRuleEngine,
        TypeCatalogService typeCatalog,
        FamilyAxisAuditService familyAxisAudit,
        PenetrationShadowService penetrationShadow,
        AuditService audit,
        SmartQcService smartQc,
        FamilyXrayService familyXray,
        SheetIntelligenceService sheetIntelligence,
        ScheduleExtractionService scheduleExtraction,
        QueryPerformanceService queryPerformance,
        SpatialIntelligenceService spatialIntelligence)
    {
        ReviewRuleEngine = reviewRuleEngine;
        TypeCatalog = typeCatalog;
        FamilyAxisAudit = familyAxisAudit;
        PenetrationShadow = penetrationShadow;
        Audit = audit;
        SmartQc = smartQc;
        FamilyXray = familyXray;
        SheetIntelligence = sheetIntelligence;
        ScheduleExtraction = scheduleExtraction;
        QueryPerformance = queryPerformance;
        SpatialIntelligence = spatialIntelligence;
    }

    internal ReviewRuleEngineService ReviewRuleEngine { get; }
    internal TypeCatalogService TypeCatalog { get; }
    internal FamilyAxisAuditService FamilyAxisAudit { get; }
    internal PenetrationShadowService PenetrationShadow { get; }
    internal AuditService Audit { get; }
    internal SmartQcService SmartQc { get; }
    internal FamilyXrayService FamilyXray { get; }
    internal SheetIntelligenceService SheetIntelligence { get; }
    internal ScheduleExtractionService ScheduleExtraction { get; }
    internal QueryPerformanceService QueryPerformance { get; }
    internal SpatialIntelligenceService SpatialIntelligence { get; }
}

/// <summary>
/// Groups hull/penetration domain services.
/// </summary>
internal sealed class HullBundle
{
    internal HullBundle(
        HullSourceCollector hullCollector,
        HullPlanner hullPlanner,
        HullValidationService hullValidator)
    {
        HullCollector = hullCollector;
        HullPlanner = hullPlanner;
        HullValidator = hullValidator;
    }

    internal HullSourceCollector HullCollector { get; }
    internal HullPlanner HullPlanner { get; }
    internal HullValidationService HullValidator { get; }
}

/// <summary>
/// Groups workflow, fix-loop, delivery, template analysis, sheet/view/export services.
/// </summary>
internal sealed class WorkflowBundle
{
    internal WorkflowBundle(
        WorkflowRuntimeService workflowRuntime,
        FixLoopService fixLoop,
        DeliveryOpsService deliveryOps,
        TemplateSheetAnalysisService templateSheetAnalysis,
        SheetViewManagementService sheetView,
        DataExportService dataExport)
    {
        WorkflowRuntime = workflowRuntime;
        FixLoop = fixLoop;
        DeliveryOps = deliveryOps;
        TemplateSheetAnalysis = templateSheetAnalysis;
        SheetView = sheetView;
        DataExport = dataExport;
    }

    internal WorkflowRuntimeService WorkflowRuntime { get; }
    internal FixLoopService FixLoop { get; }
    internal DeliveryOpsService DeliveryOps { get; }
    internal TemplateSheetAnalysisService TemplateSheetAnalysis { get; }
    internal SheetViewManagementService SheetView { get; }
    internal DataExportService DataExport { get; }
}

/// <summary>
/// Groups copilot task + worker services.
/// </summary>
internal sealed class CopilotBundle
{
    internal CopilotBundle(
        CopilotTaskService copilotTasks,
        WorkerService worker)
    {
        CopilotTasks = copilotTasks;
        Worker = worker;
    }

    internal CopilotTaskService CopilotTasks { get; }
    internal WorkerService Worker { get; }
}
