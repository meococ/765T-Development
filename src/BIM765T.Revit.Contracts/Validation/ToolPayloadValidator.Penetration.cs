using System;
using System.Collections.Generic;
using System.Linq;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Contracts.Validation;

public static partial class ToolPayloadValidator
{
    private static void ValidatePenetrationInventory(PenetrationInventoryRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(request.FamilyName))
        {
            diagnostics.Add(DiagnosticRecord.Create("PENETRATION_FAMILY_REQUIRED", DiagnosticSeverity.Error, "FamilyName must not be empty."));
        }

        if (request.MaxResults <= 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("MAX_RESULTS_INVALID", DiagnosticSeverity.Error, "MaxResults must be greater than 0."));
        }
    }

    private static void ValidateCreatePenetrationInventorySchedule(CreatePenetrationInventoryScheduleRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(request.FamilyName))
        {
            diagnostics.Add(DiagnosticRecord.Create("PENETRATION_FAMILY_REQUIRED", DiagnosticSeverity.Error, "FamilyName must not be empty."));
        }

        if (string.IsNullOrWhiteSpace(request.ScheduleName))
        {
            diagnostics.Add(DiagnosticRecord.Create("SCHEDULE_NAME_REQUIRED", DiagnosticSeverity.Error, "ScheduleName must not be empty."));
        }
    }

    private static void ValidateCreateRoundInventorySchedule(CreateRoundInventoryScheduleRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(request.FamilyName))
        {
            diagnostics.Add(DiagnosticRecord.Create("ROUND_FAMILY_REQUIRED", DiagnosticSeverity.Error, "FamilyName must not be empty."));
        }

        if (string.IsNullOrWhiteSpace(request.ScheduleName))
        {
            diagnostics.Add(DiagnosticRecord.Create("SCHEDULE_NAME_REQUIRED", DiagnosticSeverity.Error, "ScheduleName must not be empty."));
        }
    }

    private static void ValidatePenetrationRoundShadowPlan(PenetrationRoundShadowPlanRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(request.SourceFamilyName))
        {
            diagnostics.Add(DiagnosticRecord.Create("SOURCE_FAMILY_REQUIRED", DiagnosticSeverity.Error, "SourceFamilyName must not be empty."));
        }

        if (string.IsNullOrWhiteSpace(request.RoundFamilyName))
        {
            diagnostics.Add(DiagnosticRecord.Create("ROUND_FAMILY_REQUIRED", DiagnosticSeverity.Error, "RoundFamilyName must not be empty."));
        }

        if (request.MaxResults <= 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("MAX_RESULTS_INVALID", DiagnosticSeverity.Error, "MaxResults must be greater than 0."));
        }
    }

    private static void ValidateRoundExternalizationPlan(RoundExternalizationPlanRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(request.ParentFamilyName))
        {
            diagnostics.Add(DiagnosticRecord.Create("PARENT_FAMILY_REQUIRED", DiagnosticSeverity.Error, "ParentFamilyName must not be empty."));
        }

        if (string.IsNullOrWhiteSpace(request.RoundFamilyName))
        {
            diagnostics.Add(DiagnosticRecord.Create("ROUND_FAMILY_REQUIRED", DiagnosticSeverity.Error, "RoundFamilyName must not be empty."));
        }

        if (request.MaxResults <= 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("MAX_RESULTS_INVALID", DiagnosticSeverity.Error, "MaxResults must be greater than 0."));
        }

        if (request.AngleToleranceDegrees < 0 || request.AngleToleranceDegrees > 180)
        {
            diagnostics.Add(DiagnosticRecord.Create("ANGLE_TOLERANCE_INVALID", DiagnosticSeverity.Error, "AngleToleranceDegrees must be in range [0, 180]."));
        }

        if (string.IsNullOrWhiteSpace(request.TraceCommentPrefix))
        {
            diagnostics.Add(DiagnosticRecord.Create("TRACE_PREFIX_REQUIRED", DiagnosticSeverity.Error, "TraceCommentPrefix must not be empty."));
        }

        if (string.IsNullOrWhiteSpace(request.PlanWrapperFamilyName) ||
            string.IsNullOrWhiteSpace(request.ElevXWrapperFamilyName) ||
            string.IsNullOrWhiteSpace(request.ElevYWrapperFamilyName))
        {
            diagnostics.Add(DiagnosticRecord.Create("WRAPPER_FAMILY_REQUIRED", DiagnosticSeverity.Error, "Wrapper family name for clean Round AXIS_X/AXIS_Z/AXIS_Y must not be empty."));
        }

        if (string.IsNullOrWhiteSpace(request.PlanWrapperTypeName) ||
            string.IsNullOrWhiteSpace(request.ElevXWrapperTypeName) ||
            string.IsNullOrWhiteSpace(request.ElevYWrapperTypeName))
        {
            diagnostics.Add(DiagnosticRecord.Create("WRAPPER_TYPE_REQUIRED", DiagnosticSeverity.Error, "Wrapper type name for clean Round AXIS_X/AXIS_Z/AXIS_Y must not be empty."));
        }
    }

    private static void ValidateBuildRoundProjectWrappers(BuildRoundProjectWrappersRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(request.SourceFamilyName))
        {
            diagnostics.Add(DiagnosticRecord.Create("SOURCE_FAMILY_REQUIRED", DiagnosticSeverity.Error, "SourceFamilyName must not be empty."));
        }

        if (string.IsNullOrWhiteSpace(request.PlanWrapperFamilyName) ||
            string.IsNullOrWhiteSpace(request.ElevXWrapperFamilyName) ||
            string.IsNullOrWhiteSpace(request.ElevYWrapperFamilyName))
        {
            diagnostics.Add(DiagnosticRecord.Create("WRAPPER_FAMILY_REQUIRED", DiagnosticSeverity.Error, "Wrapper family name for clean Round AXIS_X/AXIS_Z/AXIS_Y must not be empty."));
        }

        if (string.IsNullOrWhiteSpace(request.PlanWrapperTypeName) ||
            string.IsNullOrWhiteSpace(request.ElevXWrapperTypeName) ||
            string.IsNullOrWhiteSpace(request.ElevYWrapperTypeName))
        {
            diagnostics.Add(DiagnosticRecord.Create("WRAPPER_TYPE_REQUIRED", DiagnosticSeverity.Error, "Wrapper type name for clean Round AXIS_X/AXIS_Z/AXIS_Y must not be empty."));
        }

        var familyNames = new[]
        {
            request.PlanWrapperFamilyName?.Trim(),
            request.ElevXWrapperFamilyName?.Trim(),
            request.ElevYWrapperFamilyName?.Trim()
        }.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        if (familyNames.Count != familyNames.Distinct(StringComparer.OrdinalIgnoreCase).Count() &&
            string.IsNullOrWhiteSpace(request.PlanWrapperFamilyName) == false &&
            !(string.Equals(request.PlanWrapperFamilyName?.Trim(), request.ElevXWrapperFamilyName?.Trim(), StringComparison.OrdinalIgnoreCase) &&
              string.Equals(request.PlanWrapperFamilyName?.Trim(), request.ElevYWrapperFamilyName?.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            diagnostics.Add(DiagnosticRecord.Create("WRAPPER_FAMILY_NAMES_DUPLICATE", DiagnosticSeverity.Error, "Wrapper family names may only be duplicated when all 3 modes use the same single family."));
        }

        var typeNames = new[]
        {
            request.PlanWrapperTypeName?.Trim(),
            request.ElevXWrapperTypeName?.Trim(),
            request.ElevYWrapperTypeName?.Trim()
        }.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        if (typeNames.Count != typeNames.Distinct(StringComparer.OrdinalIgnoreCase).Count())
        {
            diagnostics.Add(DiagnosticRecord.Create("WRAPPER_TYPE_NAMES_DUPLICATE", DiagnosticSeverity.Error, "All 3 wrapper type names must be distinct."));
        }
    }

    private static void ValidateCreateRoundShadowBatch(CreateRoundShadowBatchRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(request.SourceFamilyName))
        {
            diagnostics.Add(DiagnosticRecord.Create("SOURCE_FAMILY_REQUIRED", DiagnosticSeverity.Error, "SourceFamilyName must not be empty."));
        }

        if (string.IsNullOrWhiteSpace(request.RoundFamilyName))
        {
            diagnostics.Add(DiagnosticRecord.Create("ROUND_FAMILY_REQUIRED", DiagnosticSeverity.Error, "RoundFamilyName must not be empty."));
        }

        if (request.MaxResults <= 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("MAX_RESULTS_INVALID", DiagnosticSeverity.Error, "MaxResults must be greater than 0."));
        }

        if (request.SourceElementIds != null && request.SourceElementIds.Any(x => x <= 0))
        {
            diagnostics.Add(DiagnosticRecord.Create("SOURCE_ELEMENT_ID_INVALID", DiagnosticSeverity.Error, "SourceElementIds must only contain values greater than 0."));
        }

        if (request.ReferenceRoundElementId.HasValue && request.ReferenceRoundElementId.Value <= 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("REFERENCE_ROUND_ID_INVALID", DiagnosticSeverity.Error, "ReferenceRoundElementId must be greater than 0."));
        }

        if (request.RoundSymbolId.HasValue && request.RoundSymbolId.Value <= 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("ROUND_SYMBOL_ID_INVALID", DiagnosticSeverity.Error, "RoundSymbolId must be greater than 0."));
        }

        if (request.SetCommentsTrace && string.IsNullOrWhiteSpace(request.TraceCommentPrefix))
        {
            diagnostics.Add(DiagnosticRecord.Create("TRACE_PREFIX_REQUIRED", DiagnosticSeverity.Error, "TraceCommentPrefix must not be empty when SetCommentsTrace is true."));
        }

        if (!IsAllowedValue(request.PlacementMode, "host_face_project_aligned", "host_face_vertical_project_aligned"))
        {
            diagnostics.Add(DiagnosticRecord.Create("ROUND_SHADOW_PLACEMENT_MODE_INVALID", DiagnosticSeverity.Error, "PlacementMode only supports: host_face_project_aligned."));
        }
    }

    private static void ValidateSyncPenetrationAlphaNestedTypes(SyncPenetrationAlphaNestedTypesRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(request.ParentFamilyName))
        {
            diagnostics.Add(DiagnosticRecord.Create("PARENT_FAMILY_REQUIRED", DiagnosticSeverity.Error, "ParentFamilyName must not be empty."));
        }

        if (string.IsNullOrWhiteSpace(request.NestedFamilyName))
        {
            diagnostics.Add(DiagnosticRecord.Create("NESTED_FAMILY_REQUIRED", DiagnosticSeverity.Error, "NestedFamilyName must not be empty."));
        }
    }

    private static void ValidateRoundShadowCleanup(RoundShadowCleanupRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (request.MaxResults <= 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("MAX_RESULTS_INVALID", DiagnosticSeverity.Error, "MaxResults must be greater than 0."));
        }

        if (request.ElementIds != null && request.ElementIds.Any(x => x <= 0))
        {
            diagnostics.Add(DiagnosticRecord.Create("CLEANUP_ELEMENT_ID_INVALID", DiagnosticSeverity.Error, "ElementIds must only contain values greater than 0."));
        }

        if (!request.UseLatestSuccessfulBatchWhenEmpty && string.IsNullOrWhiteSpace(request.JournalId) && (request.ElementIds == null || request.ElementIds.Count == 0))
        {
            diagnostics.Add(DiagnosticRecord.Create("CLEANUP_SCOPE_REQUIRED", DiagnosticSeverity.Error, "JournalId or ElementIds required when UseLatestSuccessfulBatchWhenEmpty is false."));
        }

        if (request.RequireTraceCommentMatch && string.IsNullOrWhiteSpace(request.TraceCommentPrefix))
        {
            diagnostics.Add(DiagnosticRecord.Create("TRACE_PREFIX_REQUIRED", DiagnosticSeverity.Error, "TraceCommentPrefix must not be empty when RequireTraceCommentMatch is true."));
        }
    }

    private static void ValidateRoundPenetrationCutPlan(RoundPenetrationCutPlanRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        ValidateRoundPenetrationCommon(
            request.TargetFamilyName,
            request.SourceElementClasses,
            request.HostElementClasses,
            request.SourceFamilyNameContains,
            request.SourceElementIds,
            request.GybClearancePerSideInches,
            request.WfrClearancePerSideInches,
            request.AxisToleranceDegrees,
            request.TraceCommentPrefix,
            request.MaxResults,
            diagnostics);
    }

    private static void ValidateCreateRoundPenetrationCutBatch(CreateRoundPenetrationCutBatchRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        ValidateRoundPenetrationCommon(
            request.TargetFamilyName,
            request.SourceElementClasses,
            request.HostElementClasses,
            request.SourceFamilyNameContains,
            request.SourceElementIds,
            request.GybClearancePerSideInches,
            request.WfrClearancePerSideInches,
            request.AxisToleranceDegrees,
            request.TraceCommentPrefix,
            request.MaxResults,
            diagnostics);

        if (request.MaxCutRetries < 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("ROUND_PEN_CUT_RETRY_INVALID", DiagnosticSeverity.Error, "MaxCutRetries must be >= 0."));
        }

        if (request.RetryBackoffMs < 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("ROUND_PEN_CUT_BACKOFF_INVALID", DiagnosticSeverity.Error, "RetryBackoffMs must be >= 0."));
        }
    }

    private static void ValidateRoundPenetrationCutQc(RoundPenetrationCutQcRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        ValidateRoundPenetrationCommon(
            request.TargetFamilyName,
            request.SourceElementClasses,
            request.HostElementClasses,
            request.SourceFamilyNameContains,
            request.SourceElementIds,
            request.GybClearancePerSideInches,
            request.WfrClearancePerSideInches,
            request.AxisToleranceDegrees,
            request.TraceCommentPrefix,
            request.MaxResults,
            diagnostics);
    }

    private static void ValidateRoundPenetrationReviewPacket(RoundPenetrationReviewPacketRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        ValidateRoundPenetrationCommon(
            request.TargetFamilyName,
            request.SourceElementClasses,
            request.HostElementClasses,
            request.SourceFamilyNameContains,
            request.SourceElementIds,
            request.GybClearancePerSideInches,
            request.WfrClearancePerSideInches,
            request.AxisToleranceDegrees,
            request.TraceCommentPrefix,
            request.MaxResults,
            diagnostics);

        if (request.PenetrationElementIds != null && request.PenetrationElementIds.Any(x => x <= 0))
        {
            diagnostics.Add(DiagnosticRecord.Create("ROUND_PEN_REVIEW_OPENING_ID_INVALID", DiagnosticSeverity.Error, "PenetrationElementIds must only contain values greater than 0."));
        }

        if (request.MaxItems <= 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("ROUND_PEN_REVIEW_MAX_ITEMS_INVALID", DiagnosticSeverity.Error, "MaxItems must be greater than 0."));
        }

        if (string.IsNullOrWhiteSpace(request.ViewNamePrefix))
        {
            diagnostics.Add(DiagnosticRecord.Create("ROUND_PEN_REVIEW_VIEW_PREFIX_REQUIRED", DiagnosticSeverity.Error, "ViewNamePrefix must not be empty."));
        }

        if (string.IsNullOrWhiteSpace(request.SheetNumber))
        {
            diagnostics.Add(DiagnosticRecord.Create("ROUND_PEN_REVIEW_SHEET_NUMBER_REQUIRED", DiagnosticSeverity.Error, "SheetNumber must not be empty."));
        }

        if (string.IsNullOrWhiteSpace(request.SheetName))
        {
            diagnostics.Add(DiagnosticRecord.Create("ROUND_PEN_REVIEW_SHEET_NAME_REQUIRED", DiagnosticSeverity.Error, "SheetName must not be empty."));
        }

        if (request.SectionBoxPaddingFeet < 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("ROUND_PEN_REVIEW_PADDING_INVALID", DiagnosticSeverity.Error, "SectionBoxPaddingFeet must be >= 0."));
        }
    }

    private static void ValidateRoundPenetrationCommon(
        string targetFamilyName,
        ICollection<string>? sourceElementClasses,
        ICollection<string>? hostElementClasses,
        ICollection<string>? sourceFamilyNameContains,
        ICollection<int>? sourceElementIds,
        double gybClearancePerSideInches,
        double wfrClearancePerSideInches,
        double axisToleranceDegrees,
        string traceCommentPrefix,
        int maxResults,
        ICollection<DiagnosticRecord> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(targetFamilyName))
        {
            diagnostics.Add(DiagnosticRecord.Create("ROUND_PEN_TARGET_FAMILY_REQUIRED", DiagnosticSeverity.Error, "TargetFamilyName must not be empty."));
        }

        RequireNonEmpty(sourceElementClasses, "ROUND_PEN_SOURCE_CLASSES_EMPTY", "SourceElementClasses must have at least 1 value.", diagnostics);
        RequireNonEmpty(hostElementClasses, "ROUND_PEN_HOST_CLASSES_EMPTY", "HostElementClasses must have at least 1 value.", diagnostics);
        RequireNonEmpty(sourceFamilyNameContains, "ROUND_PEN_SOURCE_FAMILY_TOKENS_EMPTY", "SourceFamilyNameContains must have at least 1 value.", diagnostics);

        if (sourceElementIds != null && sourceElementIds.Any(x => x <= 0))
        {
            diagnostics.Add(DiagnosticRecord.Create("ROUND_PEN_SOURCE_ELEMENT_ID_INVALID", DiagnosticSeverity.Error, "SourceElementIds must only contain values greater than 0."));
        }

        if (gybClearancePerSideInches < 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("ROUND_PEN_GYB_CLEARANCE_INVALID", DiagnosticSeverity.Error, "GybClearancePerSideInches must be >= 0."));
        }

        if (wfrClearancePerSideInches < 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("ROUND_PEN_WFR_CLEARANCE_INVALID", DiagnosticSeverity.Error, "WfrClearancePerSideInches must be >= 0."));
        }

        if (axisToleranceDegrees < 0 || axisToleranceDegrees > 180)
        {
            diagnostics.Add(DiagnosticRecord.Create("ROUND_PEN_AXIS_TOLERANCE_INVALID", DiagnosticSeverity.Error, "AxisToleranceDegrees must be in range [0, 180]."));
        }

        if (string.IsNullOrWhiteSpace(traceCommentPrefix))
        {
            diagnostics.Add(DiagnosticRecord.Create("TRACE_PREFIX_REQUIRED", DiagnosticSeverity.Error, "TraceCommentPrefix must not be empty."));
        }

        if (maxResults <= 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("MAX_RESULTS_INVALID", DiagnosticSeverity.Error, "MaxResults must be greater than 0."));
        }
    }
}
