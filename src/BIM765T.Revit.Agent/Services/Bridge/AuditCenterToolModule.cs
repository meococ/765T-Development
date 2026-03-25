using System;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Agent.Services.Bridge;

/// <summary>
/// Audit center tap trung standards/QC/compliance thay vi de tool audit nam rai rac.
/// Public tool names giu nguyen, nhung registration va ownership duoc gom lai.
/// </summary>
internal sealed class AuditCenterToolModule : IToolModule
{
    private readonly ToolModuleContext _context;

    internal AuditCenterToolModule(ToolModuleContext context)
    {
        _context = context;
    }

    public void Register(ToolRegistry registry)
    {
        var platform = _context.Platform;
        var audit = _context.Audit;
        var analysis = _context.TemplateSheetAnalysis;
        var smartQc = _context.SmartQc;
        var reviewRuleEngine = _context.ReviewRuleEngine;
        var auditRead = ToolManifestPresets.Review("document")
            .WithRiskTags("qc")
            .WithDomainGroup("audit")
            .WithTaskFamily("audit_qc")
            .WithPackId("bim765t.agents.specialist.audit")
            .WithRecommendedPlaybooks("sheet_review_team_standard.v1");
        var auditViewRead = auditRead.WithRequiredContext("document", "view").WithTouchesActiveView();
        var auditSheetRead = auditRead.WithRequiredContext("document", "view", "sheet").WithTouchesActiveView();
        var auditMutation = ToolManifestPresets.Mutation("document")
            .WithRiskTags("qc")
            .WithDomainGroup("audit")
            .WithTaskFamily("audit_qc")
            .WithPackId("bim765t.agents.specialist.audit")
            .WithRecommendedPlaybooks("sheet_review_team_standard.v1");

        registry.Register(ToolNames.ReviewModelHealth,
            "Review model health summary, warnings, links, and recent activity.",
            PermissionLevel.Review, ApprovalRequirement.None, false, auditRead.WithRulePackTags("model_health"),
            (uiapp, request) =>
            {
                var doc = platform.ResolveDocument(uiapp, request.TargetDocument);
                var health = platform.ReviewModelHealth(uiapp, doc);
                return ToolResponses.Success(request, health, reviewSummary: health.Review);
            });

        registry.Register(ToolNames.ReviewSheetSummary,
            "Review a sheet: title block, placed views, schedules, and required sheet parameters.",
            PermissionLevel.Review, ApprovalRequirement.None, false, auditSheetRead,
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<SheetSummaryRequest>(request);
                var doc = platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey);
                var result = platform.ReviewSheetSummary(uiapp, doc, payload);
                return ToolResponses.Success(request, result, reviewSummary: result.Review);
            },
            "{\"SheetId\":null,\"SheetNumber\":\"\",\"SheetName\":\"\",\"MaxPlacedViews\":20,\"RequiredParameterNames\":[]}");

        registry.Register(ToolNames.ReviewRunRuleSet,
            "Run the review rule engine against document/view/selection scope.",
            PermissionLevel.Review, ApprovalRequirement.None, false, auditViewRead.WithRulePackTags("rule_set", "document_health_v1"),
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<ReviewRuleSetRunRequest>(request);
                var doc = platform.ResolveDocument(uiapp, request.TargetDocument);
                var response = reviewRuleEngine.Run(uiapp, platform, doc, payload, request.TargetView);
                return ToolResponses.Success(request, response, reviewSummary: response.Review);
            },
            "{\"RuleSetName\":\"document_health_v1\",\"ViewId\":null,\"ElementIds\":[],\"RequiredParameterNames\":[],\"UseCurrentSelectionWhenEmpty\":true,\"MaxIssues\":100,\"SheetId\":null,\"SheetNumber\":\"\",\"SheetName\":\"\"}");

        registry.Register(
            ToolNames.ReviewSmartQc,
            "Run smart QC by aggregating model health, standards, naming, duplicates, and optional sheet checks into machine-readable findings.",
            PermissionLevel.Read,
            ApprovalRequirement.None,
            false,
            auditRead.WithBatchMode("chunked").WithRulePackTags("smart_qc"),
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<SmartQcRequest>(request);
                var doc = platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey);
                return ToolResponses.Success(request, smartQc.Run(uiapp, doc, payload));
            },
            "{\"DocumentKey\":\"\",\"RulesetName\":\"base-rules\",\"NamingPattern\":\"\",\"SheetIds\":[],\"SheetNumbers\":[],\"RequiredParameterNames\":[],\"MaxFindings\":100,\"MaxSheets\":20,\"MaxNamingViolations\":25,\"DuplicateToleranceMm\":1.0}");

        registry.Register(ToolNames.AuditNamingConvention,
            "Audit naming conventions for views, sheets, or families against expected patterns.",
            PermissionLevel.Read, ApprovalRequirement.None, false, auditRead,
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<NamingAuditRequest>(request);
                var doc = platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey);
                return ToolResponses.Success(request, audit.AuditNaming(platform, doc, payload));
            },
            "{\"DocumentKey\":\"\",\"CategoryNames\":[],\"ExpectedPattern\":\"\",\"Scope\":\"views\",\"MaxResults\":500}");

        registry.Register(ToolNames.AuditUnusedViews,
            "Find views not placed on any sheet - candidates for cleanup or purge.",
            PermissionLevel.Read, ApprovalRequirement.None, false, auditRead,
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<UnusedViewsRequest>(request);
                var doc = platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey);
                return ToolResponses.Success(request, audit.AuditUnusedViews(platform, doc, payload));
            },
            "{\"DocumentKey\":\"\",\"IncludeSchedules\":false,\"IncludeLegends\":false,\"ExcludeTemplates\":true,\"MaxResults\":500}");

        registry.Register(ToolNames.AuditUnusedFamilies,
            "Find families with zero instances in the model - candidates for purge to reduce file size.",
            PermissionLevel.Read, ApprovalRequirement.None, false, auditRead,
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<UnusedFamiliesRequest>(request);
                var doc = platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey);
                return ToolResponses.Success(request, audit.AuditUnusedFamilies(platform, doc, payload));
            },
            "{\"DocumentKey\":\"\",\"CategoryNames\":[],\"IncludeSystemFamilies\":false,\"MaxResults\":500}");

        registry.Register(ToolNames.AuditDuplicateElements,
            "Detect overlapping/duplicate elements at the same location with same type.",
            PermissionLevel.Read, ApprovalRequirement.None, false, auditRead,
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<DuplicateElementsRequest>(request);
                var doc = platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey);
                return ToolResponses.Success(request, audit.AuditDuplicates(platform, doc, payload));
            },
            "{\"DocumentKey\":\"\",\"CategoryNames\":[\"Walls\",\"Columns\"],\"ToleranceMm\":1.0,\"MaxResults\":200}");

        registry.Register(ToolNames.AuditWarningsCleanupPlan,
            "Analyze all model warnings, categorize them, and suggest cleanup actions with auto-fix availability.",
            PermissionLevel.Read, ApprovalRequirement.None, false, auditRead,
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<WarningsCleanupRequest>(request);
                var doc = platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey);
                return ToolResponses.Success(request, audit.AuditWarnings(platform, doc, payload));
            },
            "{\"DocumentKey\":\"\",\"SeverityFilter\":\"\",\"CategoryFilter\":\"\",\"MaxResults\":200}");

        registry.Register(ToolNames.AuditModelStandards,
            "Check model against BIM standards: project info, workset naming, warning count, unused views, grid/level naming.",
            PermissionLevel.Read, ApprovalRequirement.None, false, auditRead,
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<ModelStandardsRequest>(request);
                var doc = platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey);
                return ToolResponses.Success(request, audit.AuditModelStandards(platform, doc, payload));
            },
            "{\"DocumentKey\":\"\",\"RuleNames\":[]}");

        registry.RegisterMutationTool<PurgeUnusedRequest>(
            ToolNames.AuditPurgeUnusedSafe,
            "Purge unused views and families to reduce file size. High-risk operation with dry-run preview.",
            ApprovalRequirement.HighRiskToken,
            "{\"DocumentKey\":\"\",\"PurgeViews\":true,\"PurgeFamilies\":true,\"PurgeMaterials\":false,\"PurgeLinePatterns\":false,\"ExcludeNames\":[]}",
            auditMutation.WithRiskTags("delete"),
            () => platform.Settings.AllowDeleteTools, StatusCodes.DeleteDisabled,
            (uiapp, request, payload) => platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey),
            null,
            (uiapp, services, doc, payload, request) => audit.PreviewPurgeUnused(uiapp, services, doc, payload, request),
            (uiapp, services, doc, payload) => audit.ExecutePurgeUnused(uiapp, services, doc, payload));

        registry.Register(ToolNames.AuditComplianceReport,
            "Generate a comprehensive compliance report covering naming, unused elements, warnings, standards, and duplicates.",
            PermissionLevel.Read, ApprovalRequirement.None, false, auditRead,
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<ComplianceReportRequest>(request);
                var doc = platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey);
                return ToolResponses.Success(request, audit.GenerateComplianceReport(platform, doc, payload));
            },
            "{\"DocumentKey\":\"\",\"IncludeNaming\":true,\"IncludeUnused\":true,\"IncludeWarnings\":true,\"IncludeStandards\":true,\"IncludeDuplicates\":true}");

        registry.Register(ToolNames.AuditTemplateHealth,
            "Analyze view template health: unused%, duplicates, naming violations, overall grade (A-F).",
            PermissionLevel.Read, ApprovalRequirement.None, false, auditRead,
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<TemplateHealthRequest>(request);
                var doc = platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey);
                return ToolResponses.Success(request, analysis.AuditTemplateHealth(platform, doc, payload));
            },
            "{\"DocumentKey\":\"\",\"NameContains\":\"\",\"ViewType\":\"\",\"NamingPattern\":\"\",\"DuplicateSimilarityThreshold\":0.85,\"MaxResults\":500}");

        registry.Register(ToolNames.AuditSheetOrganization,
            "Analyze sheet organization: auto-group by naming, detect empty/heavy/missing-titleblock sheets, grade (A-F).",
            PermissionLevel.Read, ApprovalRequirement.None, false, auditRead.WithRequiredContext("document", "sheet"),
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<SheetOrganizationRequest>(request);
                var doc = platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey);
                return ToolResponses.Success(request, analysis.AuditSheetOrganization(platform, doc, payload));
            },
            "{\"DocumentKey\":\"\",\"GroupByPattern\":\"\",\"HeavySheetThreshold\":10,\"MaxResults\":500}");

        registry.Register(ToolNames.AuditTemplateSheetMap,
            "Map template->view->sheet chains. Find broken chains (views not on sheets), orphan templates, views without templates.",
            PermissionLevel.Read, ApprovalRequirement.None, false, auditRead,
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<TemplateSheetMapRequest>(request);
                var doc = platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey);
                return ToolResponses.Success(request, analysis.BuildTemplateSheetMap(platform, doc, payload));
            },
            "{\"DocumentKey\":\"\",\"TemplateNameContains\":\"\",\"OnlyBrokenChains\":false,\"MaxResults\":200}");

        registry.Register(ToolNames.AuditViewTemplateCompliance,
            "Master compliance audit combining template health + sheet organization + chain integrity + naming. Returns overall score (0-100) and grade (A-F) with top recommendations.",
            PermissionLevel.Read, ApprovalRequirement.None, false, auditRead,
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<ViewTemplateComplianceRequest>(request);
                var doc = platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey);
                return ToolResponses.Success(request, analysis.AuditViewTemplateCompliance(platform, doc, payload));
            },
            "{\"DocumentKey\":\"\",\"IncludeTemplateHealth\":true,\"IncludeSheetOrganization\":true,\"IncludeChainAnalysis\":true,\"NamingPattern\":\"\"}");
    }
}
