using System.Collections.Generic;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Contracts.Validation;

public static partial class ToolPayloadValidator
{
    // ── Workset Operations ────────────────────────────────────────────────────

    private static void ValidateWorksetCreate(WorksetCreateRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            diagnostics.Add(DiagnosticRecord.Create("WORKSET_NAME_REQUIRED", DiagnosticSeverity.Error, "Name không được rỗng."));
        }
    }

    private static void ValidateWorksetBulkReassign(WorksetBulkReassignRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(request.TargetWorksetName))
        {
            diagnostics.Add(DiagnosticRecord.Create("WORKSET_TARGET_NAME_REQUIRED", DiagnosticSeverity.Error, "TargetWorksetName không được rỗng."));
        }
    }

    private static void ValidateWorksetOpenClose(WorksetOpenCloseRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(request.WorksetName))
        {
            diagnostics.Add(DiagnosticRecord.Create("WORKSET_NAME_REQUIRED", DiagnosticSeverity.Error, "WorksetName không được rỗng."));
        }
    }

    // ── View Crop Operations ──────────────────────────────────────────────────

    private static void ValidateViewSetCropRegion(ViewSetCropRegionRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (request.ViewId <= 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("VIEW_ID_REQUIRED", DiagnosticSeverity.Error, "ViewId phải > 0."));
        }

        if (request.MaxX <= request.MinX)
        {
            diagnostics.Add(DiagnosticRecord.Create("CROP_REGION_X_INVALID", DiagnosticSeverity.Error, "MaxX phải lớn hơn MinX."));
        }

        if (request.MaxY <= request.MinY)
        {
            diagnostics.Add(DiagnosticRecord.Create("CROP_REGION_Y_INVALID", DiagnosticSeverity.Error, "MaxY phải lớn hơn MinY."));
        }
    }

    private static void ValidateViewSetViewRange(ViewSetViewRangeRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (request.ViewId <= 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("VIEW_ID_REQUIRED", DiagnosticSeverity.Error, "ViewId phải > 0."));
        }

        if (request.TopOffset <= request.CutPlaneOffset)
        {
            diagnostics.Add(DiagnosticRecord.Create("VIEW_RANGE_TOP_INVALID", DiagnosticSeverity.Error, "TopOffset phải lớn hơn CutPlaneOffset."));
        }

        if (request.ViewDepthOffset > request.BottomOffset)
        {
            diagnostics.Add(DiagnosticRecord.Create("VIEW_RANGE_DEPTH_INVALID", DiagnosticSeverity.Error, "ViewDepthOffset phải nhỏ hơn hoặc bằng BottomOffset."));
        }
    }

    // ── Schedule Compare ──────────────────────────────────────────────────────

    private static void ValidateScheduleCompare(ScheduleCompareRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (request.ScheduleViewId <= 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("SCHEDULE_VIEW_ID_REQUIRED", DiagnosticSeverity.Error, "ScheduleViewId phải > 0."));
        }

        if (string.IsNullOrWhiteSpace(request.BaselineSnapshotJson))
        {
            diagnostics.Add(DiagnosticRecord.Create("BASELINE_SNAPSHOT_REQUIRED", DiagnosticSeverity.Error, "BaselineSnapshotJson không được rỗng."));
        }
    }

    // ── Revision Operations ───────────────────────────────────────────────────

    private static void ValidateRevisionCreate(RevisionCreateRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(request.Description))
        {
            diagnostics.Add(DiagnosticRecord.Create("REVISION_DESCRIPTION_REQUIRED", DiagnosticSeverity.Error, "Description không được rỗng."));
        }

        if (!IsAllowedValue(request.Numbering, "numeric", "alphanumeric"))
        {
            diagnostics.Add(DiagnosticRecord.Create("REVISION_NUMBERING_INVALID", DiagnosticSeverity.Error, "Numbering phải là 'numeric' hoặc 'alphanumeric'."));
        }
    }

    private static void ValidateRevisionList(RevisionListRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        // No constraints beyond DocumentKey, which is checked at dispatch layer.
    }
}
