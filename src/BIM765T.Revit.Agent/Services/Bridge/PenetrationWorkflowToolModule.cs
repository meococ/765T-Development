using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Agent.Services.Bridge;

internal sealed class PenetrationWorkflowToolModule : IToolModule
{
    private readonly ToolModuleContext _context;

    internal PenetrationWorkflowToolModule(ToolModuleContext context)
    {
        _context = context;
    }

    public void Register(ToolRegistry registry)
    {
        var platform = _context.Platform;
        var penetrationShadow = _context.PenetrationShadow;
        var readDocument = ToolManifestPresets.Read("document");
        var mutationDocument = ToolManifestPresets.Mutation("document");
        var batchMutationDocument = mutationDocument.WithBatchMode("chunked");

        registry.Register(ToolNames.ReportPenetrationAlphaInventory, "Inventory all Penetration Alpha family instances, key Mii variables, host, type, and axis status.", PermissionLevel.Read, ApprovalRequirement.None, false, readDocument,
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<PenetrationInventoryRequest>(request);
                var doc = platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey);
                var result = penetrationShadow.ReportInventory(uiapp, platform, doc, payload);
                return ToolResponses.Success(request, result, reviewSummary: result.Review);
            },
            "{\"DocumentKey\":\"\",\"FamilyName\":\"Penetration Alpha\",\"ViewId\":null,\"ViewName\":\"\",\"MaxResults\":5000,\"IncludeAxisStatus\":true}");

        registry.Register(ToolNames.ReportPenetrationRoundShadowPlan, "Plan Round shadow instances for each Penetration Alpha using Mii_Diameter, Mii_DimLength, Mii_ElementClass, and Mii_ElementTier.", PermissionLevel.Read, ApprovalRequirement.None, false, readDocument,
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<PenetrationRoundShadowPlanRequest>(request);
                var doc = platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey);
                var result = penetrationShadow.PlanRoundShadow(uiapp, platform, doc, payload);
                return ToolResponses.Success(request, result, reviewSummary: result.Review);
            },
            "{\"DocumentKey\":\"\",\"SourceFamilyName\":\"Penetration Alpha\",\"RoundFamilyName\":\"Round\",\"PreferredReferenceMark\":\"test\",\"MaxResults\":5000}");

        registry.Register(ToolNames.ReportRoundExternalizationPlan, "Plan how to externalize nested Round instances out of Penetration Alpha into one clean project-level Round family with type axes AXIS_X / AXIS_Z / AXIS_Y.", PermissionLevel.Read, ApprovalRequirement.None, false, readDocument,
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<RoundExternalizationPlanRequest>(request);
                var doc = platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey);
                var result = penetrationShadow.PlanRoundExternalization(platform, doc, payload);
                return ToolResponses.Success(request, result, reviewSummary: result.Review);
            },
            "{\"DocumentKey\":\"\",\"ParentFamilyName\":\"Penetration Alpha\",\"RoundFamilyName\":\"Round\",\"MaxResults\":10000,\"AngleToleranceDegrees\":5.0,\"RequireParentFamilyMatch\":true,\"TraceCommentPrefix\":\"BIM765T_EXTERNAL_ROUND\",\"PlanWrapperFamilyName\":\"Round_Project\",\"PlanWrapperTypeName\":\"AXIS_X\",\"ElevXWrapperFamilyName\":\"Round_Project\",\"ElevXWrapperTypeName\":\"AXIS_Z\",\"ElevYWrapperFamilyName\":\"Round_Project\",\"ElevYWrapperTypeName\":\"AXIS_Y\"}");

        registry.RegisterMutationTool<BuildRoundProjectWrappersRequest>(
            ToolNames.FamilyBuildRoundProjectWrappersSafe,
            "Build and load one clean project-level Round family with native geometry types AXIS_X / AXIS_Z / AXIS_Y for safe IFC externalization review.",
            ApprovalRequirement.HighRiskToken,
            "{\"DocumentKey\":\"\",\"SourceFamilyName\":\"Round\",\"OutputDirectory\":\"\",\"OverwriteFamilyFiles\":true,\"LoadIntoProject\":true,\"OverwriteExistingProjectFamilies\":true,\"PlanWrapperFamilyName\":\"Round_Project\",\"PlanWrapperTypeName\":\"AXIS_X\",\"ElevXWrapperFamilyName\":\"Round_Project\",\"ElevXWrapperTypeName\":\"AXIS_Z\",\"ElevYWrapperFamilyName\":\"Round_Project\",\"ElevYWrapperTypeName\":\"AXIS_Y\",\"GenerateSizeSpecificVariants\":false}",
            mutationDocument,
            () => platform.Settings.AllowWriteTools,
            StatusCodes.WriteDisabled,
            (uiapp, request, payload) => platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey),
            null,
            (_, services, doc, payload, request) => penetrationShadow.PreviewBuildRoundProjectWrappers(services, doc, payload, request),
            (_, services, doc, payload) => penetrationShadow.ExecuteBuildRoundProjectWrappers(services, doc, payload));

        registry.RegisterMutationTool<SyncPenetrationAlphaNestedTypesRequest>(
            ToolNames.FamilySyncPenetrationAlphaNestedTypesSafe,
            "In family document Penetration Alpha M, create missing nested Penetration Alpha types to match every parent family type name and map the nested child type per parent type, then optionally reload back into the open project.",
            ApprovalRequirement.HighRiskToken,
            "{\"DocumentKey\":\"\",\"ParentFamilyName\":\"Penetration Alpha M\",\"NestedFamilyName\":\"Penetration Alpha\",\"ProjectDocumentKey\":\"\",\"ReloadIntoProject\":true,\"OverwriteExistingProjectFamily\":true,\"RequireSingleNestedInstance\":true,\"PreferredSeedTypeName\":\"\"}",
            mutationDocument,
            () => platform.Settings.AllowWriteTools,
            StatusCodes.WriteDisabled,
            (uiapp, request, payload) => platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey),
            null,
            (uiapp, services, doc, payload, request) => penetrationShadow.PreviewSyncPenetrationAlphaNestedTypes(uiapp, services, doc, payload, request),
            (uiapp, services, doc, payload) => penetrationShadow.ExecuteSyncPenetrationAlphaNestedTypes(uiapp, services, doc, payload));

        registry.Register(ToolNames.ReportRoundShadowCleanupPlan, "Report cleanup candidates for previously created Round shadow instances using trace comments or the latest successful batch journal.", PermissionLevel.Read, ApprovalRequirement.None, false, readDocument,
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<RoundShadowCleanupRequest>(request);
                var doc = platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey);
                var result = penetrationShadow.ReportRoundShadowCleanupPlan(platform, doc, payload);
                return ToolResponses.Success(request, result, reviewSummary: result.Review);
            },
            "{\"DocumentKey\":\"\",\"TraceCommentPrefix\":\"BIM765T_SHADOW_ROUND\",\"JournalId\":\"\",\"ElementIds\":[],\"UseLatestSuccessfulBatchWhenEmpty\":true,\"RequireTraceCommentMatch\":true,\"MaxResults\":5000}");

        registry.Register(ToolNames.ReportRoundPenetrationCutPlan, "Plan round penetration openings for pipe-like source families that clash with GYB/WFR cassette hosts using instance->type fallback for Mii_ElementClass and cassette prefilter.", PermissionLevel.Read, ApprovalRequirement.None, false,
            readDocument,
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<RoundPenetrationCutPlanRequest>(request);
                var doc = platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey);
                var result = penetrationShadow.PlanRoundPenetrationCut(platform, doc, payload);
                return ToolResponses.Success(request, result, reviewSummary: result.Review);
            },
            "{\"DocumentKey\":\"\",\"TargetFamilyName\":\"Mii_Pen-Round_Project\",\"SourceElementClasses\":[\"PIP\",\"PPF\",\"PPG\"],\"HostElementClasses\":[\"GYB\",\"WFR\"],\"SourceFamilyNameContains\":[\"PIP\",\"PPF\",\"PPG\"],\"SourceElementIds\":[],\"GybClearancePerSideInches\":0.25,\"WfrClearancePerSideInches\":0.125,\"AxisToleranceDegrees\":5.0,\"TraceCommentPrefix\":\"BIM765T_PEN_ROUND\",\"MaxResults\":5000,\"IncludeExisting\":true}");

        registry.Register(ToolNames.ReportRoundPenetrationCutQc, "QC the round penetration opening workflow: verify placed openings, void cuts, local-X/source-axis alignment, residuals, and orphan traced instances.", PermissionLevel.Read, ApprovalRequirement.None, false,
            readDocument,
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<RoundPenetrationCutQcRequest>(request);
                var doc = platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey);
                var result = penetrationShadow.ReportRoundPenetrationCutQc(platform, doc, payload);
                return ToolResponses.Success(request, result, reviewSummary: result.Review);
            },
            "{\"DocumentKey\":\"\",\"TargetFamilyName\":\"Mii_Pen-Round_Project\",\"SourceElementClasses\":[\"PIP\",\"PPF\",\"PPG\"],\"HostElementClasses\":[\"GYB\",\"WFR\"],\"SourceFamilyNameContains\":[\"PIP\",\"PPF\",\"PPG\"],\"SourceElementIds\":[],\"GybClearancePerSideInches\":0.25,\"WfrClearancePerSideInches\":0.125,\"AxisToleranceDegrees\":5.0,\"TraceCommentPrefix\":\"BIM765T_PEN_ROUND\",\"MaxResults\":5000}");

        registry.RegisterMutationTool<RoundPenetrationReviewPacketRequest>(
            ToolNames.ReviewRoundPenetrationPacketSafe,
            "Create dedicated 3D review views for round penetration cases, color source/opening/host, place them on a review sheet, and optionally export a snapshot image.",
            ApprovalRequirement.ConfirmToken,
            "{\"DocumentKey\":\"\",\"TargetFamilyName\":\"Mii_Pen-Round_Project\",\"SourceElementClasses\":[\"PIP\",\"PPF\",\"PPG\"],\"HostElementClasses\":[\"GYB\",\"WFR\"],\"SourceFamilyNameContains\":[\"PIP\",\"PPF\",\"PPG\"],\"SourceElementIds\":[],\"PenetrationElementIds\":[],\"GybClearancePerSideInches\":0.25,\"WfrClearancePerSideInches\":0.125,\"AxisToleranceDegrees\":5.0,\"TraceCommentPrefix\":\"BIM765T_PEN_ROUND\",\"MaxResults\":5000,\"MaxItems\":6,\"ViewNamePrefix\":\"BIM765T_RoundPen_Review\",\"SheetNumber\":\"BIM765T-RP-01\",\"SheetName\":\"Round Penetration Review\",\"TitleBlockTypeName\":\"\",\"SectionBoxPaddingFeet\":0.5,\"CopyActive3DOrientation\":true,\"ReuseExistingViews\":true,\"ReuseExistingSheet\":true,\"ExportSheetImage\":true,\"ImageOutputPath\":\"\",\"ActivateSheetAfterCreate\":false,\"IncludeOnlyNonOkQcItems\":true}",
            mutationDocument,
            () => platform.Settings.AllowWriteTools,
            StatusCodes.WriteDisabled,
            (uiapp, request, payload) => platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey),
            null,
            (uiapp, services, doc, payload, request) => penetrationShadow.PreviewCreateRoundPenetrationReviewPacket(uiapp, services, doc, payload, request),
            (uiapp, services, doc, payload) => penetrationShadow.ExecuteCreateRoundPenetrationReviewPacket(uiapp, services, doc, payload));

        registry.RegisterMutationTool<CreatePenetrationInventoryScheduleRequest>(
            ToolNames.ScheduleCreatePenetrationAlphaInventorySafe,
            "Create/update a project schedule inventory for Penetration Alpha instances.",
            ApprovalRequirement.ConfirmToken,
            "{\"DocumentKey\":\"\",\"FamilyName\":\"Penetration Alpha\",\"ScheduleName\":\"BIM765T_PenetrationAlpha_Inventory\",\"OverwriteIfExists\":true,\"Itemized\":true}",
            mutationDocument,
            () => platform.Settings.AllowWriteTools,
            StatusCodes.WriteDisabled,
            (uiapp, request, payload) => platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey),
            null,
            (_, services, doc, payload, request) => penetrationShadow.PreviewCreateInventorySchedule(services, doc, payload, request),
            (_, services, doc, payload) => penetrationShadow.ExecuteCreateInventorySchedule(services, doc, payload));

        registry.RegisterMutationTool<CreateRoundInventoryScheduleRequest>(
            ToolNames.ScheduleCreateRoundInventorySafe,
            "Create/update a project schedule listing all Round family instances — columns: Family, Type, Count, Level, Mark, Mii_Diameter, Mii_DimLength, Mii_ElementClass, Mii_ElementTier, Comments. Filtered by FamilyName (default='Round').",
            ApprovalRequirement.ConfirmToken,
            "{\"DocumentKey\":\"\",\"FamilyName\":\"Round\",\"ScheduleName\":\"BIM765T_Round_Inventory\",\"OverwriteIfExists\":true,\"Itemized\":true,\"IncludeLinkedFiles\":false}",
            mutationDocument,
            () => platform.Settings.AllowWriteTools,
            StatusCodes.WriteDisabled,
            (uiapp, request, payload) => platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey),
            null,
            (_, services, doc, payload, request) => penetrationShadow.PreviewCreateRoundInventorySchedule(services, doc, payload, request),
            (_, services, doc, payload) => penetrationShadow.ExecuteCreateRoundInventorySchedule(services, doc, payload));

        registry.RegisterMutationTool<CreateRoundShadowBatchRequest>(
            ToolNames.BatchCreateRoundShadowSafe,
            "Create aligned Round shadow instances for each Penetration Alpha source using safe placement primitives (no CopyElement).",
            ApprovalRequirement.ConfirmToken,
            "{\"DocumentKey\":\"\",\"SourceFamilyName\":\"Penetration Alpha\",\"RoundFamilyName\":\"Round\",\"PreferredReferenceMark\":\"test\",\"MaxResults\":5000,\"SourceElementIds\":[],\"ReferenceRoundElementId\":null,\"RoundSymbolId\":null,\"TraceCommentPrefix\":\"BIM765T_SHADOW_ROUND\",\"SetCommentsTrace\":true,\"CopyDiameter\":true,\"CopyLength\":true,\"CopyElementClass\":true,\"CopyElementTier\":true,\"SkipIfTraceExists\":true,\"PlacementMode\":\"host_face_project_aligned\",\"RequireAxisAlignedResult\":true}",
            batchMutationDocument,
            () => platform.Settings.AllowWriteTools,
            StatusCodes.WriteDisabled,
            (uiapp, request, payload) => platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey),
            null,
            (_, services, doc, payload, request) => penetrationShadow.PreviewCreateRoundShadowBatch(services, doc, payload, request),
            (_, services, doc, payload) => penetrationShadow.ExecuteCreateRoundShadowBatch(services, doc, payload));

        registry.RegisterMutationTool<CreateRoundPenetrationCutBatchRequest>(
            ToolNames.BatchCreateRoundPenetrationCutSafe,
            "Build/load clean round penetration opening families, place aligned hostless instances at pipe/host intersections, and apply safe void cuts on GYB/WFR hosts with residual reporting.",
            ApprovalRequirement.ConfirmToken,
            "{\"DocumentKey\":\"\",\"TargetFamilyName\":\"Mii_Pen-Round_Project\",\"SourceElementClasses\":[\"PIP\",\"PPF\",\"PPG\"],\"HostElementClasses\":[\"GYB\",\"WFR\"],\"SourceFamilyNameContains\":[\"PIP\",\"PPF\",\"PPG\"],\"SourceElementIds\":[],\"GybClearancePerSideInches\":0.25,\"WfrClearancePerSideInches\":0.125,\"AxisToleranceDegrees\":5.0,\"TraceCommentPrefix\":\"BIM765T_PEN_ROUND\",\"MaxResults\":5000,\"OutputDirectory\":\"\",\"OverwriteFamilyFiles\":true,\"OverwriteExistingProjectFamilies\":true,\"ForceRebuildFamilies\":false,\"SetCommentsTrace\":true,\"RequireAxisAlignedResult\":true,\"MaxCutRetries\":2,\"RetryBackoffMs\":150,\"ShowReviewBodyByDefault\":false}",
            batchMutationDocument,
            () => platform.Settings.AllowWriteTools,
            StatusCodes.WriteDisabled,
            (uiapp, request, payload) => platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey),
            null,
            (_, services, doc, payload, request) => penetrationShadow.PreviewCreateRoundPenetrationCutBatch(services, doc, payload, request),
            (_, services, doc, payload) => penetrationShadow.ExecuteCreateRoundPenetrationCutBatch(services, doc, payload));

        registry.RegisterMutationTool<RoundShadowCleanupRequest>(
            ToolNames.CleanupRoundShadowByRunSafe,
            "Delete only traced Round shadow instances from the latest batch or explicit ids after a guarded preview.",
            ApprovalRequirement.HighRiskToken,
            "{\"DocumentKey\":\"\",\"TraceCommentPrefix\":\"BIM765T_SHADOW_ROUND\",\"JournalId\":\"\",\"ElementIds\":[],\"UseLatestSuccessfulBatchWhenEmpty\":true,\"RequireTraceCommentMatch\":true,\"MaxResults\":5000}",
            batchMutationDocument.WithRiskTags("delete"),
            () => platform.Settings.AllowWriteTools,
            StatusCodes.WriteDisabled,
            (uiapp, request, payload) => platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey),
            null,
            (_, services, doc, payload, request) => penetrationShadow.PreviewCleanupRoundShadow(services, doc, payload, request),
            (_, services, doc, payload) => penetrationShadow.ExecuteCleanupRoundShadow(services, doc, payload));
    }
}
