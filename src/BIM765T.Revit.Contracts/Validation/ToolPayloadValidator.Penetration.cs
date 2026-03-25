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
            diagnostics.Add(DiagnosticRecord.Create("PENETRATION_FAMILY_REQUIRED", DiagnosticSeverity.Error, "FamilyName không được rỗng."));
        }

        if (request.MaxResults <= 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("MAX_RESULTS_INVALID", DiagnosticSeverity.Error, "MaxResults phải > 0."));
        }
    }

    private static void ValidateCreatePenetrationInventorySchedule(CreatePenetrationInventoryScheduleRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(request.FamilyName))
        {
            diagnostics.Add(DiagnosticRecord.Create("PENETRATION_FAMILY_REQUIRED", DiagnosticSeverity.Error, "FamilyName không được rỗng."));
        }

        if (string.IsNullOrWhiteSpace(request.ScheduleName))
        {
            diagnostics.Add(DiagnosticRecord.Create("SCHEDULE_NAME_REQUIRED", DiagnosticSeverity.Error, "ScheduleName không được rỗng."));
        }
    }

    private static void ValidateCreateRoundInventorySchedule(CreateRoundInventoryScheduleRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(request.FamilyName))
        {
            diagnostics.Add(DiagnosticRecord.Create("ROUND_FAMILY_REQUIRED", DiagnosticSeverity.Error, "FamilyName không được rỗng."));
        }

        if (string.IsNullOrWhiteSpace(request.ScheduleName))
        {
            diagnostics.Add(DiagnosticRecord.Create("SCHEDULE_NAME_REQUIRED", DiagnosticSeverity.Error, "ScheduleName không được rỗng."));
        }
    }

    private static void ValidatePenetrationRoundShadowPlan(PenetrationRoundShadowPlanRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(request.SourceFamilyName))
        {
            diagnostics.Add(DiagnosticRecord.Create("SOURCE_FAMILY_REQUIRED", DiagnosticSeverity.Error, "SourceFamilyName không được rỗng."));
        }

        if (string.IsNullOrWhiteSpace(request.RoundFamilyName))
        {
            diagnostics.Add(DiagnosticRecord.Create("ROUND_FAMILY_REQUIRED", DiagnosticSeverity.Error, "RoundFamilyName không được rỗng."));
        }

        if (request.MaxResults <= 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("MAX_RESULTS_INVALID", DiagnosticSeverity.Error, "MaxResults phải > 0."));
        }
    }

    private static void ValidateRoundExternalizationPlan(RoundExternalizationPlanRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(request.ParentFamilyName))
        {
            diagnostics.Add(DiagnosticRecord.Create("PARENT_FAMILY_REQUIRED", DiagnosticSeverity.Error, "ParentFamilyName không được rỗng."));
        }

        if (string.IsNullOrWhiteSpace(request.RoundFamilyName))
        {
            diagnostics.Add(DiagnosticRecord.Create("ROUND_FAMILY_REQUIRED", DiagnosticSeverity.Error, "RoundFamilyName không được rỗng."));
        }

        if (request.MaxResults <= 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("MAX_RESULTS_INVALID", DiagnosticSeverity.Error, "MaxResults phải > 0."));
        }

        if (request.AngleToleranceDegrees < 0 || request.AngleToleranceDegrees > 180)
        {
            diagnostics.Add(DiagnosticRecord.Create("ANGLE_TOLERANCE_INVALID", DiagnosticSeverity.Error, "AngleToleranceDegrees phải nằm trong [0, 180]."));
        }

        if (string.IsNullOrWhiteSpace(request.TraceCommentPrefix))
        {
            diagnostics.Add(DiagnosticRecord.Create("TRACE_PREFIX_REQUIRED", DiagnosticSeverity.Error, "TraceCommentPrefix không được rỗng."));
        }

        if (string.IsNullOrWhiteSpace(request.PlanWrapperFamilyName) ||
            string.IsNullOrWhiteSpace(request.ElevXWrapperFamilyName) ||
            string.IsNullOrWhiteSpace(request.ElevYWrapperFamilyName))
        {
            diagnostics.Add(DiagnosticRecord.Create("WRAPPER_FAMILY_REQUIRED", DiagnosticSeverity.Error, "Tên wrapper family cho clean Round AXIS_X/AXIS_Z/AXIS_Y không được rỗng."));
        }

        if (string.IsNullOrWhiteSpace(request.PlanWrapperTypeName) ||
            string.IsNullOrWhiteSpace(request.ElevXWrapperTypeName) ||
            string.IsNullOrWhiteSpace(request.ElevYWrapperTypeName))
        {
            diagnostics.Add(DiagnosticRecord.Create("WRAPPER_TYPE_REQUIRED", DiagnosticSeverity.Error, "Tên wrapper type cho clean Round AXIS_X/AXIS_Z/AXIS_Y không được rỗng."));
        }
    }

    private static void ValidateBuildRoundProjectWrappers(BuildRoundProjectWrappersRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(request.SourceFamilyName))
        {
            diagnostics.Add(DiagnosticRecord.Create("SOURCE_FAMILY_REQUIRED", DiagnosticSeverity.Error, "SourceFamilyName không được rỗng."));
        }

        if (string.IsNullOrWhiteSpace(request.PlanWrapperFamilyName) ||
            string.IsNullOrWhiteSpace(request.ElevXWrapperFamilyName) ||
            string.IsNullOrWhiteSpace(request.ElevYWrapperFamilyName))
        {
            diagnostics.Add(DiagnosticRecord.Create("WRAPPER_FAMILY_REQUIRED", DiagnosticSeverity.Error, "Tên wrapper family cho clean Round AXIS_X/AXIS_Z/AXIS_Y không được rỗng."));
        }

        if (string.IsNullOrWhiteSpace(request.PlanWrapperTypeName) ||
            string.IsNullOrWhiteSpace(request.ElevXWrapperTypeName) ||
            string.IsNullOrWhiteSpace(request.ElevYWrapperTypeName))
        {
            diagnostics.Add(DiagnosticRecord.Create("WRAPPER_TYPE_REQUIRED", DiagnosticSeverity.Error, "Tên wrapper type cho clean Round AXIS_X/AXIS_Z/AXIS_Y không được rỗng."));
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
            diagnostics.Add(DiagnosticRecord.Create("WRAPPER_FAMILY_NAMES_DUPLICATE", DiagnosticSeverity.Error, "Wrapper family names chỉ được duplicate khi cả 3 mode cùng dùng 1 family duy nhất."));
        }

        var typeNames = new[]
        {
            request.PlanWrapperTypeName?.Trim(),
            request.ElevXWrapperTypeName?.Trim(),
            request.ElevYWrapperTypeName?.Trim()
        }.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        if (typeNames.Count != typeNames.Distinct(StringComparer.OrdinalIgnoreCase).Count())
        {
            diagnostics.Add(DiagnosticRecord.Create("WRAPPER_TYPE_NAMES_DUPLICATE", DiagnosticSeverity.Error, "3 wrapper type names phải khác nhau."));
        }
    }

    private static void ValidateCreateRoundShadowBatch(CreateRoundShadowBatchRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(request.SourceFamilyName))
        {
            diagnostics.Add(DiagnosticRecord.Create("SOURCE_FAMILY_REQUIRED", DiagnosticSeverity.Error, "SourceFamilyName không được rỗng."));
        }

        if (string.IsNullOrWhiteSpace(request.RoundFamilyName))
        {
            diagnostics.Add(DiagnosticRecord.Create("ROUND_FAMILY_REQUIRED", DiagnosticSeverity.Error, "RoundFamilyName không được rỗng."));
        }

        if (request.MaxResults <= 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("MAX_RESULTS_INVALID", DiagnosticSeverity.Error, "MaxResults phải > 0."));
        }

        if (request.SourceElementIds != null && request.SourceElementIds.Any(x => x <= 0))
        {
            diagnostics.Add(DiagnosticRecord.Create("SOURCE_ELEMENT_ID_INVALID", DiagnosticSeverity.Error, "SourceElementIds chỉ được chứa ElementId > 0."));
        }

        if (request.ReferenceRoundElementId.HasValue && request.ReferenceRoundElementId.Value <= 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("REFERENCE_ROUND_ID_INVALID", DiagnosticSeverity.Error, "ReferenceRoundElementId phải > 0."));
        }

        if (request.RoundSymbolId.HasValue && request.RoundSymbolId.Value <= 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("ROUND_SYMBOL_ID_INVALID", DiagnosticSeverity.Error, "RoundSymbolId phải > 0."));
        }

        if (request.SetCommentsTrace && string.IsNullOrWhiteSpace(request.TraceCommentPrefix))
        {
            diagnostics.Add(DiagnosticRecord.Create("TRACE_PREFIX_REQUIRED", DiagnosticSeverity.Error, "TraceCommentPrefix không được rỗng khi SetCommentsTrace = true."));
        }

        if (!IsAllowedValue(request.PlacementMode, "host_face_project_aligned", "host_face_vertical_project_aligned"))
        {
            diagnostics.Add(DiagnosticRecord.Create("ROUND_SHADOW_PLACEMENT_MODE_INVALID", DiagnosticSeverity.Error, "PlacementMode chỉ hỗ trợ: host_face_project_aligned."));
        }
    }

    private static void ValidateSyncPenetrationAlphaNestedTypes(SyncPenetrationAlphaNestedTypesRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(request.ParentFamilyName))
        {
            diagnostics.Add(DiagnosticRecord.Create("PARENT_FAMILY_REQUIRED", DiagnosticSeverity.Error, "ParentFamilyName không được rỗng."));
        }

        if (string.IsNullOrWhiteSpace(request.NestedFamilyName))
        {
            diagnostics.Add(DiagnosticRecord.Create("NESTED_FAMILY_REQUIRED", DiagnosticSeverity.Error, "NestedFamilyName không được rỗng."));
        }
    }

    private static void ValidateRoundShadowCleanup(RoundShadowCleanupRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (request.MaxResults <= 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("MAX_RESULTS_INVALID", DiagnosticSeverity.Error, "MaxResults phải > 0."));
        }

        if (request.ElementIds != null && request.ElementIds.Any(x => x <= 0))
        {
            diagnostics.Add(DiagnosticRecord.Create("CLEANUP_ELEMENT_ID_INVALID", DiagnosticSeverity.Error, "ElementIds chỉ được chứa ElementId > 0."));
        }

        if (!request.UseLatestSuccessfulBatchWhenEmpty && string.IsNullOrWhiteSpace(request.JournalId) && (request.ElementIds == null || request.ElementIds.Count == 0))
        {
            diagnostics.Add(DiagnosticRecord.Create("CLEANUP_SCOPE_REQUIRED", DiagnosticSeverity.Error, "Cần JournalId hoặc ElementIds khi không dùng UseLatestSuccessfulBatchWhenEmpty."));
        }

        if (request.RequireTraceCommentMatch && string.IsNullOrWhiteSpace(request.TraceCommentPrefix))
        {
            diagnostics.Add(DiagnosticRecord.Create("TRACE_PREFIX_REQUIRED", DiagnosticSeverity.Error, "TraceCommentPrefix không được rỗng khi RequireTraceCommentMatch = true."));
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
            diagnostics.Add(DiagnosticRecord.Create("ROUND_PEN_CUT_RETRY_INVALID", DiagnosticSeverity.Error, "MaxCutRetries phai >= 0."));
        }

        if (request.RetryBackoffMs < 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("ROUND_PEN_CUT_BACKOFF_INVALID", DiagnosticSeverity.Error, "RetryBackoffMs phai >= 0."));
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
            diagnostics.Add(DiagnosticRecord.Create("ROUND_PEN_REVIEW_OPENING_ID_INVALID", DiagnosticSeverity.Error, "PenetrationElementIds chi duoc chua ElementId > 0."));
        }

        if (request.MaxItems <= 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("ROUND_PEN_REVIEW_MAX_ITEMS_INVALID", DiagnosticSeverity.Error, "MaxItems phai > 0."));
        }

        if (string.IsNullOrWhiteSpace(request.ViewNamePrefix))
        {
            diagnostics.Add(DiagnosticRecord.Create("ROUND_PEN_REVIEW_VIEW_PREFIX_REQUIRED", DiagnosticSeverity.Error, "ViewNamePrefix khong duoc rong."));
        }

        if (string.IsNullOrWhiteSpace(request.SheetNumber))
        {
            diagnostics.Add(DiagnosticRecord.Create("ROUND_PEN_REVIEW_SHEET_NUMBER_REQUIRED", DiagnosticSeverity.Error, "SheetNumber khong duoc rong."));
        }

        if (string.IsNullOrWhiteSpace(request.SheetName))
        {
            diagnostics.Add(DiagnosticRecord.Create("ROUND_PEN_REVIEW_SHEET_NAME_REQUIRED", DiagnosticSeverity.Error, "SheetName khong duoc rong."));
        }

        if (request.SectionBoxPaddingFeet < 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("ROUND_PEN_REVIEW_PADDING_INVALID", DiagnosticSeverity.Error, "SectionBoxPaddingFeet phai >= 0."));
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
            diagnostics.Add(DiagnosticRecord.Create("ROUND_PEN_TARGET_FAMILY_REQUIRED", DiagnosticSeverity.Error, "TargetFamilyName khong duoc rong."));
        }

        RequireNonEmpty(sourceElementClasses, "ROUND_PEN_SOURCE_CLASSES_EMPTY", "SourceElementClasses phai co it nhat 1 gia tri.", diagnostics);
        RequireNonEmpty(hostElementClasses, "ROUND_PEN_HOST_CLASSES_EMPTY", "HostElementClasses phai co it nhat 1 gia tri.", diagnostics);
        RequireNonEmpty(sourceFamilyNameContains, "ROUND_PEN_SOURCE_FAMILY_TOKENS_EMPTY", "SourceFamilyNameContains phai co it nhat 1 gia tri.", diagnostics);

        if (sourceElementIds != null && sourceElementIds.Any(x => x <= 0))
        {
            diagnostics.Add(DiagnosticRecord.Create("ROUND_PEN_SOURCE_ELEMENT_ID_INVALID", DiagnosticSeverity.Error, "SourceElementIds chi duoc chua ElementId > 0."));
        }

        if (gybClearancePerSideInches < 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("ROUND_PEN_GYB_CLEARANCE_INVALID", DiagnosticSeverity.Error, "GybClearancePerSideInches phai >= 0."));
        }

        if (wfrClearancePerSideInches < 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("ROUND_PEN_WFR_CLEARANCE_INVALID", DiagnosticSeverity.Error, "WfrClearancePerSideInches phai >= 0."));
        }

        if (axisToleranceDegrees < 0 || axisToleranceDegrees > 180)
        {
            diagnostics.Add(DiagnosticRecord.Create("ROUND_PEN_AXIS_TOLERANCE_INVALID", DiagnosticSeverity.Error, "AxisToleranceDegrees phai nam trong [0, 180]."));
        }

        if (string.IsNullOrWhiteSpace(traceCommentPrefix))
        {
            diagnostics.Add(DiagnosticRecord.Create("TRACE_PREFIX_REQUIRED", DiagnosticSeverity.Error, "TraceCommentPrefix khong duoc rong."));
        }

        if (maxResults <= 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("MAX_RESULTS_INVALID", DiagnosticSeverity.Error, "MaxResults phai > 0."));
        }
    }
}
