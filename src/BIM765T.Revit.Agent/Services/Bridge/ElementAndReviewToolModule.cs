using System;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Agent.Services.Bridge;

internal sealed class ElementAndReviewToolModule : IToolModule
{
    private readonly ToolModuleContext _context;

    internal ElementAndReviewToolModule(ToolModuleContext context)
    {
        _context = context;
    }

    public void Register(ToolRegistry registry)
    {
        var platform = _context.Platform;
        var familyAxisAudit = _context.FamilyAxisAudit;
        var elementRead = ToolManifestPresets.Read("document");
        var qcReviewDocument = ToolManifestPresets.Review("document").WithRiskTags("qc");
        var qcReviewDocumentView = ToolManifestPresets.Review("document", "view").WithTouchesActiveView().WithRiskTags("qc");
        var qcReviewDocumentSheet = ToolManifestPresets.Review("document", "view", "sheet").WithTouchesActiveView().WithRiskTags("qc");
        var qcSnapshotReview = qcReviewDocumentSheet.WithPreviewArtifacts("snapshot");
        var batchAxisReview = qcReviewDocumentView.WithBatchMode("chunked");

        registry.Register(ToolNames.ElementQuery, "Query elements by scope/category/class/id.", PermissionLevel.Read, ApprovalRequirement.None, false, elementRead,
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<ElementQueryRequest>(request);
                var doc = platform.ResolveDocument(uiapp, request.TargetDocument);
                return ToolResponses.Success(request, platform.QueryElements(uiapp, doc, payload));
            });
        registry.Register(ToolNames.ElementInspect, "Inspect elements and optionally include parameters.", PermissionLevel.Read, ApprovalRequirement.None, false, elementRead,
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<ElementQueryRequest>(request);
                payload.IncludeParameters = true;
                var doc = platform.ResolveDocument(uiapp, request.TargetDocument);
                return ToolResponses.Success(request, platform.QueryElements(uiapp, doc, payload));
            });

        registry.Register(ToolNames.ReviewModelWarnings, "Review current model warnings.", PermissionLevel.Review, ApprovalRequirement.None, false, qcReviewDocument,
            (uiapp, request) =>
            {
                var doc = platform.ResolveDocument(uiapp, request.TargetDocument);
                var review = platform.ReviewWarnings(doc);
                return ToolResponses.Success(request, review, reviewSummary: review);
            });
        registry.Register(ToolNames.ReviewActiveViewSummary, "Review active or target view with category/class counts.", PermissionLevel.Review, ApprovalRequirement.None, false, qcReviewDocumentView,
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<ActiveViewSummaryRequest>(request);
                var doc = platform.ResolveDocument(uiapp, request.TargetDocument);
                var summary = platform.ReviewActiveViewSummary(uiapp, doc, payload, request.TargetView);
                return ToolResponses.Success(request, summary);
            });
        registry.Register(ToolNames.ReviewLinksStatus, "Review Revit link load status in target document.", PermissionLevel.Review, ApprovalRequirement.None, false, qcReviewDocument,
            (uiapp, request) =>
            {
                var doc = platform.ResolveDocument(uiapp, request.TargetDocument);
                return ToolResponses.Success(request, platform.ReviewLinksStatus(doc));
            });
        registry.Register(ToolNames.ReviewWorksetHealth, "Review active workset, selection workset hygiene, and common workset mistakes.", PermissionLevel.Review, ApprovalRequirement.None, false, qcReviewDocument,
            (uiapp, request) =>
            {
                var doc = platform.ResolveDocument(uiapp, request.TargetDocument);
                var result = platform.ReviewWorksetHealth(uiapp, doc);
                return ToolResponses.Success(request, result, reviewSummary: result.Review);
            });
        registry.Register(ToolNames.ReviewCaptureSnapshot, "Capture a structured snapshot for active view/sheet/selection and optionally export an image artifact.", PermissionLevel.Review, ApprovalRequirement.None, false, qcSnapshotReview,
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<CaptureSnapshotRequest>(request);
                var doc = platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey);
                var result = platform.CaptureSnapshot(uiapp, doc, payload);
                return ToolResponses.Success(request, result, reviewSummary: result.Review, artifacts: result.ArtifactPaths);
            },
            "{\"Scope\":\"active_view\",\"ViewId\":null,\"SheetId\":null,\"SheetNumber\":\"\",\"SheetName\":\"\",\"ElementIds\":[],\"IncludeParameters\":false,\"ParameterNames\":[],\"MaxElements\":100,\"ExportImage\":false,\"ImageOutputPath\":\"\",\"ImagePixelSize\":2048}");
        registry.Register(ToolNames.ReviewCadGenericModelOverlap, "Compare visible CAD imports versus Generic Models in the active/target view using a projected point-cloud fingerprint. Returns whether the two visible versions overlap 100% and previews where they differ.", PermissionLevel.Review, ApprovalRequirement.None, false, qcReviewDocumentView,
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<CadGenericModelOverlapRequest>(request);
                var doc = platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey);
                var result = platform.ReviewCadGenericModelOverlap(uiapp, doc, payload, request.TargetView);
                return ToolResponses.Success(request, result, reviewSummary: result.Review);
            },
            "{\"DocumentKey\":\"\",\"ViewId\":null,\"ViewName\":\"\",\"ImportNameContains\":\"\",\"GenericModelNameContains\":\"\",\"GenericModelFamilyNameContains\":\"\",\"ToleranceFeet\":0.00328084,\"SamplingStepFeet\":0.0328084,\"MaxElementsPerSide\":500,\"MaxPreviewPoints\":20,\"MaxSamplePointsPerSide\":150000}");
        registry.Register(ToolNames.ReviewFamilyAxisAlignment, "Check all visible family instances in the active/target view, compare their local axes to project axes, audit nested/shared transform risks, and optionally highlight mismatches via UI selection.", PermissionLevel.Review, ApprovalRequirement.None, false, qcReviewDocumentView,
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<FamilyAxisAlignmentRequest>(request);
                var doc = platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey);
                var result = familyAxisAudit.ReviewFamilyAxisAlignment(uiapp, platform, doc, payload, request.TargetView);
                return ToolResponses.Success(request, result, reviewSummary: result.Review);
            },
            "{\"DocumentKey\":\"\",\"ViewId\":null,\"ViewName\":\"\",\"CategoryNames\":[],\"AngleToleranceDegrees\":5.0,\"TreatMirroredAsMismatch\":true,\"TreatAntiParallelAsMismatch\":false,\"HighlightInUi\":true,\"IncludeAlignedItems\":false,\"MaxElements\":2000,\"MaxIssues\":200,\"ZoomToHighlighted\":false,\"AnalyzeNestedFamilies\":true,\"MaxFamilyDefinitionsToInspect\":150,\"MaxNestedInstancesPerFamily\":200,\"MaxNestedFindingsPerFamily\":20,\"TreatNonSharedNestedAsRisk\":true,\"TreatNestedMirroredAsRisk\":true,\"TreatNestedRotatedAsRisk\":true,\"TreatNestedTiltedAsRisk\":true,\"IncludeNestedFindings\":false,\"UseActiveViewOnly\":true}");
        registry.Register(ToolNames.ReviewFamilyAxisAlignmentGlobal, "Check family instance local axes across the whole document (or an explicitly requested view), compare to project axes, audit nested/shared transform risks, and skip UI highlight when document-wide scope is used.", PermissionLevel.Review, ApprovalRequirement.None, false, qcReviewDocument,
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<FamilyAxisAlignmentRequest>(request);
                payload.UseActiveViewOnly = false;
                var doc = platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey);
                var result = familyAxisAudit.ReviewFamilyAxisAlignment(uiapp, platform, doc, payload, request.TargetView);
                return ToolResponses.Success(request, result, reviewSummary: result.Review);
            },
            "{\"DocumentKey\":\"\",\"ViewId\":null,\"ViewName\":\"\",\"CategoryNames\":[],\"AngleToleranceDegrees\":5.0,\"TreatMirroredAsMismatch\":true,\"TreatAntiParallelAsMismatch\":false,\"HighlightInUi\":false,\"IncludeAlignedItems\":false,\"MaxElements\":5000,\"MaxIssues\":5000,\"ZoomToHighlighted\":false,\"AnalyzeNestedFamilies\":true,\"MaxFamilyDefinitionsToInspect\":200,\"MaxNestedInstancesPerFamily\":500,\"MaxNestedFindingsPerFamily\":100,\"TreatNonSharedNestedAsRisk\":true,\"TreatNestedMirroredAsRisk\":true,\"TreatNestedRotatedAsRisk\":true,\"TreatNestedTiltedAsRisk\":true,\"IncludeNestedFindings\":false,\"UseActiveViewOnly\":false}");
        registry.Register(ToolNames.BatchAuditAxes, "Batch alias for family axis audit across visible family instances.", PermissionLevel.Review, ApprovalRequirement.None, false, batchAxisReview,
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<FamilyAxisAlignmentRequest>(request);
                var doc = platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey);
                var result = familyAxisAudit.ReviewFamilyAxisAlignment(uiapp, platform, doc, payload, request.TargetView);
                return ToolResponses.Success(request, result, reviewSummary: result.Review);
            },
            "{\"DocumentKey\":\"\",\"ViewId\":null,\"ViewName\":\"\",\"CategoryNames\":[],\"AngleToleranceDegrees\":5.0,\"TreatMirroredAsMismatch\":true,\"TreatAntiParallelAsMismatch\":false,\"HighlightInUi\":true,\"IncludeAlignedItems\":false,\"MaxElements\":2000,\"MaxIssues\":200,\"ZoomToHighlighted\":false,\"AnalyzeNestedFamilies\":true,\"MaxFamilyDefinitionsToInspect\":150,\"MaxNestedInstancesPerFamily\":200,\"MaxNestedFindingsPerFamily\":20,\"TreatNonSharedNestedAsRisk\":true,\"TreatNestedMirroredAsRisk\":true,\"TreatNestedRotatedAsRisk\":true,\"TreatNestedTiltedAsRisk\":true,\"IncludeNestedFindings\":false}");
        registry.Register(ToolNames.ReviewParameterCompleteness, "Review missing/empty parameters.", PermissionLevel.Review, ApprovalRequirement.None, false, qcReviewDocument,
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<ReviewParameterCompletenessRequest>(request);
                var doc = platform.ResolveDocument(uiapp, request.TargetDocument);
                var report = platform.ReviewParameterCompleteness(doc, payload);
                return ToolResponses.Success(request, report, reviewSummary: report);
            });
    }
}
