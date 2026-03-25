using BIM765T.Revit.Agent.Services.Hull;
using BIM765T.Revit.Agent.Services.Platform;
using BIM765T.Revit.Agent.Services.Review;
using BIM765T.Revit.Agent.Workflow;

namespace BIM765T.Revit.Agent.Services.Bridge;

internal sealed class ToolModuleContext
{
    internal ToolModuleContext(
        PlatformBundle platformBundle,
        InspectionBundle inspectionBundle,
        HullBundle hullBundle,
        WorkflowBundle workflowBundle,
        CopilotBundle copilotBundle)
    {
        // PlatformBundle
        Platform = platformBundle.Platform;
        Mutation = platformBundle.Mutation;
        ViewAutomation = platformBundle.ViewAutomation;
        FileLifecycle = platformBundle.FileLifecycle;

        // InspectionBundle
        ReviewRuleEngine = inspectionBundle.ReviewRuleEngine;
        TypeCatalog = inspectionBundle.TypeCatalog;
        FamilyAxisAudit = inspectionBundle.FamilyAxisAudit;
        PenetrationShadow = inspectionBundle.PenetrationShadow;
        Audit = inspectionBundle.Audit;
        SmartQc = inspectionBundle.SmartQc;
        FamilyXray = inspectionBundle.FamilyXray;
        SheetIntelligence = inspectionBundle.SheetIntelligence;
        ScheduleExtraction = inspectionBundle.ScheduleExtraction;
        QueryPerformance = inspectionBundle.QueryPerformance;
        SpatialIntelligence = inspectionBundle.SpatialIntelligence;

        // HullBundle
        HullCollector = hullBundle.HullCollector;
        HullPlanner = hullBundle.HullPlanner;
        HullValidator = hullBundle.HullValidator;

        // WorkflowBundle
        WorkflowRuntime = workflowBundle.WorkflowRuntime;
        FixLoop = workflowBundle.FixLoop;
        DeliveryOps = workflowBundle.DeliveryOps;
        TemplateSheetAnalysis = workflowBundle.TemplateSheetAnalysis;
        SheetView = workflowBundle.SheetView;
        DataExport = workflowBundle.DataExport;

        // CopilotBundle
        CopilotTasks = copilotBundle.CopilotTasks;
        Worker = copilotBundle.Worker;
    }

    internal PlatformServices Platform { get; }
    internal MutationService Mutation { get; }
    internal ViewAutomationService ViewAutomation { get; }
    internal FileLifecycleService FileLifecycle { get; }
    internal ReviewRuleEngineService ReviewRuleEngine { get; }
    internal TypeCatalogService TypeCatalog { get; }
    internal FamilyAxisAuditService FamilyAxisAudit { get; }
    internal PenetrationShadowService PenetrationShadow { get; }
    internal HullSourceCollector HullCollector { get; }
    internal HullPlanner HullPlanner { get; }
    internal HullValidationService HullValidator { get; }
    internal SheetViewManagementService SheetView { get; }
    internal DataExportService DataExport { get; }
    internal ScheduleExtractionService ScheduleExtraction { get; }
    internal FamilyXrayService FamilyXray { get; }
    internal SheetIntelligenceService SheetIntelligence { get; }
    internal AuditService Audit { get; }
    internal SmartQcService SmartQc { get; }
    internal WorkflowRuntimeService WorkflowRuntime { get; }
    internal TemplateSheetAnalysisService TemplateSheetAnalysis { get; }
    internal FixLoopService FixLoop { get; }
    internal DeliveryOpsService DeliveryOps { get; }
    internal CopilotTaskService CopilotTasks { get; }
    internal QueryPerformanceService QueryPerformance { get; }
    internal SpatialIntelligenceService SpatialIntelligence { get; }
    internal WorkerService Worker { get; }
}
