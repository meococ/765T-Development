using System;
using System.Collections.Generic;
using System.Linq;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Hull;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Contracts.Validation;

public static partial class ToolPayloadValidator
{
    private static readonly IReadOnlyDictionary<Type, Action<object, ICollection<DiagnosticRecord>>> Validators = CreateValidators();

    public static void Validate<T>(T payload)
    {
        if (payload == null)
        {
            throw new ToolPayloadValidationException(
                "Tool payload is null.",
                new[]
                {
                    DiagnosticRecord.Create("PAYLOAD_NULL", DiagnosticSeverity.Error, "Payload không được null.")
                });
        }

        var diagnostics = ValidateObject(payload).ToList();
        if (diagnostics.Count > 0)
        {
            throw new ToolPayloadValidationException(diagnostics[0].Message, diagnostics);
        }
    }

    public static IReadOnlyList<DiagnosticRecord> ValidateObject<T>(T payload)
    {
        var diagnostics = new List<DiagnosticRecord>();
        if (payload == null)
        {
            diagnostics.Add(DiagnosticRecord.Create("PAYLOAD_NULL", DiagnosticSeverity.Error, "Payload không được null."));
            return diagnostics;
        }

        if (Validators.TryGetValue(payload.GetType(), out var validator))
        {
            validator(payload!, diagnostics);
        }

        return diagnostics;
    }

    private static IReadOnlyDictionary<Type, Action<object, ICollection<DiagnosticRecord>>> CreateValidators()
    {
        var validators = new Dictionary<Type, Action<object, ICollection<DiagnosticRecord>>>();
        Register<AddTextNoteRequest>(validators, ValidateAddTextNote);
        Register<UpdateTextNoteStyleRequest>(validators, ValidateUpdateTextNoteStyle);
        Register<UpdateTextNoteContentRequest>(validators, ValidateUpdateTextNoteContent);
        Register<ElementQueryRequest>(validators, ValidateElementQuery);
        Register<ReviewParameterCompletenessRequest>(validators, ValidateReviewParameterCompleteness);
        Register<ReviewRuleSetRunRequest>(validators, ValidateReviewRuleSetRun);
        Register<FixLoopPlanRequest>(validators, ValidateFixLoopPlan);
        Register<FixLoopApplyRequest>(validators, ValidateFixLoopApply);
        Register<FixLoopVerifyRequest>(validators, ValidateFixLoopVerify);
        Register<TaskPlanRequest>(validators, ValidateTaskPlan);
        Register<TaskPreviewRequest>(validators, ValidateTaskPreview);
        Register<TaskApproveStepRequest>(validators, ValidateTaskApproveStep);
        Register<TaskExecuteStepRequest>(validators, ValidateTaskExecuteStep);
        Register<TaskResumeRequest>(validators, ValidateTaskResume);
        Register<TaskVerifyRequest>(validators, ValidateTaskVerify);
        Register<TaskGetRunRequest>(validators, ValidateTaskGetRun);
        Register<TaskListRunsRequest>(validators, ValidateTaskListRuns);
        Register<TaskSummarizeRequest>(validators, ValidateTaskSummarize);
        Register<TaskMetricsRequest>(validators, ValidateTaskMetrics);
        Register<TaskResidualsRequest>(validators, ValidateTaskResiduals);
        Register<TaskPromoteMemoryRequest>(validators, ValidateTaskPromoteMemory);
        Register<ExternalTaskIntakeRequest>(validators, ValidateExternalTaskIntake);
        Register<TaskQueueEnqueueRequest>(validators, ValidateTaskQueueEnqueue);
        Register<TaskQueueClaimRequest>(validators, ValidateTaskQueueClaim);
        Register<TaskQueueCompleteRequest>(validators, ValidateTaskQueueComplete);
        Register<TaskQueueRunRequest>(validators, ValidateTaskQueueRun);
        Register<TaskQueueListRequest>(validators, ValidateTaskQueueList);
        Register<ConnectorCallbackPreviewRequest>(validators, ValidateConnectorCallbackPreview);
        Register<HotStateRequest>(validators, ValidateHotState);
        Register<ContextDeltaSummaryRequest>(validators, ValidateContextDeltaSummary);
        Register<ContextResolveBundleRequest>(validators, ValidateContextResolveBundle);
        Register<ContextSearchAnchorsRequest>(validators, ValidateContextSearchAnchors);
        Register<ArtifactSummarizeRequest>(validators, ValidateArtifactSummarize);
        Register<MemoryFindSimilarRunsRequest>(validators, ValidateMemoryFindSimilarRuns);
        Register<ToolCapabilityLookupRequest>(validators, ValidateToolCapabilityLookup);
        Register<ToolGuidanceRequest>(validators, ValidateToolGuidance);
        Register<CommandAtlasSearchRequest>(validators, ValidateCommandAtlasSearch);
        Register<CommandDescribeRequest>(validators, ValidateCommandDescribe);
        Register<CommandExecuteRequest>(validators, ValidateCommandExecute);
        Register<CoverageReportRequest>(validators, ValidateCoverageReport);
        Register<QuickActionRequest>(validators, ValidateQuickAction);
        Register<FallbackArtifactRequest>(validators, ValidateFallbackArtifactRequest);
        Register<MemoryScopedSearchRequest>(validators, ValidateMemoryScopedSearch);
        Register<WorkerMessageRequest>(validators, ValidateWorkerMessage);
        Register<WorkerSessionRequest>(validators, ValidateWorkerSession);
        Register<WorkerListSessionsRequest>(validators, ValidateWorkerListSessions);
        Register<WorkerSetPersonaRequest>(validators, ValidateWorkerSetPersona);
        Register<WorkerContextRequest>(validators, ValidateWorkerContext);
        Register<ScheduleExtractionRequest>(validators, ValidateScheduleExtraction);
        Register<SmartQcRequest>(validators, ValidateSmartQc);
        Register<FamilyXrayRequest>(validators, ValidateFamilyXray);
        Register<SheetCaptureIntelligenceRequest>(validators, ValidateSheetCaptureIntelligence);
        Register<SetParametersRequest>(validators, ValidateSetParameters);
        Register<DeleteElementsRequest>(validators, ValidateDeleteElements);
        Register<MoveElementsRequest>(validators, ValidateMoveElements);
        Register<RotateElementsRequest>(validators, ValidateRotateElements);
        Register<PlaceFamilyInstanceRequest>(validators, ValidatePlaceFamilyInstance);
        Register<SaveAsDocumentRequest>(validators, ValidateSaveAs);
        Register<OpenBackgroundDocumentRequest>(validators, ValidateOpenBackground);
        Register<CloseDocumentRequest>(validators, ValidateCloseDocument);
        Register<SynchronizeRequest>(validators, ValidateSynchronize);
        Register<SheetSummaryRequest>(validators, ValidateSheetSummary);
        Register<CaptureSnapshotRequest>(validators, ValidateCaptureSnapshot);
        Register<TaskContextRequest>(validators, ValidateTaskContext);
        Register<Create3DViewRequest>(validators, ValidateCreate3DView);
        Register<CreateProjectViewRequest>(validators, ValidateCreateProjectView);
        Register<CreateOrUpdateViewFilterRequest>(validators, ValidateCreateOrUpdateViewFilter);
        Register<ApplyViewFilterRequest>(validators, ValidateApplyViewFilter);
        Register<RemoveFilterFromViewRequest>(validators, ValidateRemoveFilterFromView);
        Register<DeleteFilterRequest>(validators, ValidateDeleteFilter);
        Register<ElementTypeQueryRequest>(validators, ValidateElementTypeQuery);
        Register<TextNoteTypeUsageRequest>(validators, ValidateTextTypeUsage);
        Register<FamilyAxisAlignmentRequest>(validators, ValidateFamilyAxisAlignment);
        Register<PenetrationInventoryRequest>(validators, ValidatePenetrationInventory);
        Register<CreatePenetrationInventoryScheduleRequest>(validators, ValidateCreatePenetrationInventorySchedule);
        Register<CreateRoundInventoryScheduleRequest>(validators, ValidateCreateRoundInventorySchedule);
        Register<PenetrationRoundShadowPlanRequest>(validators, ValidatePenetrationRoundShadowPlan);
        Register<RoundExternalizationPlanRequest>(validators, ValidateRoundExternalizationPlan);
        Register<BuildRoundProjectWrappersRequest>(validators, ValidateBuildRoundProjectWrappers);
        Register<CreateRoundShadowBatchRequest>(validators, ValidateCreateRoundShadowBatch);
        Register<SyncPenetrationAlphaNestedTypesRequest>(validators, ValidateSyncPenetrationAlphaNestedTypes);
        Register<RoundShadowCleanupRequest>(validators, ValidateRoundShadowCleanup);
        Register<RoundPenetrationCutPlanRequest>(validators, ValidateRoundPenetrationCutPlan);
        Register<CreateRoundPenetrationCutBatchRequest>(validators, ValidateCreateRoundPenetrationCutBatch);
        Register<RoundPenetrationCutQcRequest>(validators, ValidateRoundPenetrationCutQc);
        Register<RoundPenetrationReviewPacketRequest>(validators, ValidateRoundPenetrationReviewPacket);
        Register<FamilyLoadRequest>(validators, ValidateFamilyLoad);
        Register<ScheduleCreateRequest>(validators, ValidateScheduleCreate);
        Register<OutputTargetValidationRequest>(validators, ValidateOutputTargetValidation);
        Register<IfcExportRequest>(validators, ValidateIfcExport);
        Register<DwgExportRequest>(validators, ValidateDwgExport);
        Register<PdfPrintRequest>(validators, ValidatePdfPrint);
        Register<HullDryRunRequest>(validators, ValidateHullDryRun);

        // Family Authoring validators
        Register<FamilyAddParameterRequest>(validators, ValidateFamilyAddParameter);
        Register<FamilySetFormulaRequest>(validators, ValidateFamilySetFormula);
        Register<FamilySetTypeCatalogRequest>(validators, ValidateFamilySetTypeCatalog);
        Register<FamilyAddReferencePlaneRequest>(validators, ValidateFamilyAddReferencePlane);
        Register<FamilyCreateExtrusionRequest>(validators, ValidateFamilyCreateExtrusion);
        Register<FamilyCreateSweepRequest>(validators, ValidateFamilyCreateSweep);
        Register<FamilyCreateBlendRequest>(validators, ValidateFamilyCreateBlend);
        Register<FamilyCreateRevolutionRequest>(validators, ValidateFamilyCreateRevolution);
        Register<FamilySetSubcategoryRequest>(validators, ValidateFamilySetSubcategory);
        Register<FamilyLoadNestedRequest>(validators, ValidateFamilyLoadNested);
        Register<FamilySaveRequest>(validators, ValidateFamilySave);

        // Script Orchestration validators
        Register<ScriptValidateRequest>(validators, ValidateScriptValidate);
        Register<ScriptRunRequest>(validators, ValidateScriptRun);
        Register<ScriptComposeRequest>(validators, ValidateScriptCompose);
        Register<ScriptSourceVerifyRequest>(validators, ValidateScriptSourceVerify);
        Register<ScriptImportManifestRequest>(validators, ValidateScriptImportManifest);
        Register<ScriptInstallPackRequest>(validators, ValidateScriptInstallPack);

        // Wave 1 — Workset Operations
        Register<WorksetCreateRequest>(validators, ValidateWorksetCreate);
        Register<WorksetBulkReassignRequest>(validators, ValidateWorksetBulkReassign);
        Register<WorksetOpenCloseRequest>(validators, ValidateWorksetOpenClose);

        // Wave 1 — View Crop Operations
        Register<ViewSetCropRegionRequest>(validators, ValidateViewSetCropRegion);
        Register<ViewSetViewRangeRequest>(validators, ValidateViewSetViewRange);

        // Wave 1 — Schedule Compare
        Register<ScheduleCompareRequest>(validators, ValidateScheduleCompare);

        // Wave 1 — Revision Operations
        Register<RevisionCreateRequest>(validators, ValidateRevisionCreate);
        Register<RevisionListRequest>(validators, ValidateRevisionList);

        // Project init / context engine
        Register<ProjectInitPreviewRequest>(validators, ValidateProjectInitPreview);
        Register<ProjectInitApplyRequest>(validators, ValidateProjectInitApply);
        Register<ProjectManifestRequest>(validators, ValidateProjectManifest);
        Register<ProjectContextBundleRequest>(validators, ValidateProjectContextBundle);
        Register<ProjectDeepScanRequest>(validators, ValidateProjectDeepScan);
        Register<ProjectDeepScanGetRequest>(validators, ValidateProjectDeepScanGet);

        return validators;
    }

    private static void Register<T>(IDictionary<Type, Action<object, ICollection<DiagnosticRecord>>> validators, Action<T, ICollection<DiagnosticRecord>> validator)
    {
        validators[typeof(T)] = (payload, diagnostics) => validator((T)payload, diagnostics);
    }

    private static void RequireNonEmpty<T>(ICollection<T>? items, string code, string message, ICollection<DiagnosticRecord> diagnostics)
    {
        if (items == null || items.Count == 0)
        {
            diagnostics.Add(DiagnosticRecord.Create(code, DiagnosticSeverity.Error, message));
        }
    }

    private static bool IsAllowedValue(string? value, params string[] allowed)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return allowed.Any(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase));
    }

    private static void ValidateRgb(int? red, int? green, int? blue, ICollection<DiagnosticRecord> diagnostics)
    {
        ValidateRgbComponent(red, "RED_INVALID", "Red", diagnostics);
        ValidateRgbComponent(green, "GREEN_INVALID", "Green", diagnostics);
        ValidateRgbComponent(blue, "BLUE_INVALID", "Blue", diagnostics);
    }

    private static void ValidateRgbComponent(int? value, string code, string name, ICollection<DiagnosticRecord> diagnostics)
    {
        if (value.HasValue && (value.Value < 0 || value.Value > 255))
        {
            diagnostics.Add(DiagnosticRecord.Create(code, DiagnosticSeverity.Error, $"{name} phải nằm trong khoảng 0..255."));
        }
    }
}
