using System.Collections.Generic;
using System.Linq;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Contracts.Validation;

public static partial class ToolPayloadValidator
{
    private static void ValidateAddTextNote(AddTextNoteRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
        {
            diagnostics.Add(DiagnosticRecord.Create("TEXT_REQUIRED", DiagnosticSeverity.Error, "Text note phải có nội dung."));
        }
    }

    private static void ValidateUpdateTextNoteStyle(UpdateTextNoteStyleRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (request.TextNoteId <= 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("TEXT_NOTE_ID_REQUIRED", DiagnosticSeverity.Error, "TextNoteId phải > 0."));
        }

        ValidateRgb(request.Red, request.Green, request.Blue, diagnostics);
    }

    private static void ValidateUpdateTextNoteContent(UpdateTextNoteContentRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (request.TextNoteId <= 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("TEXT_NOTE_ID_REQUIRED", DiagnosticSeverity.Error, "TextNoteId phải > 0."));
        }

        if (string.IsNullOrWhiteSpace(request.NewText))
        {
            diagnostics.Add(DiagnosticRecord.Create("NEW_TEXT_REQUIRED", DiagnosticSeverity.Error, "NewText không được rỗng."));
        }
    }

    private static void ValidateElementQuery(ElementQueryRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (request.MaxResults <= 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("MAX_RESULTS_INVALID", DiagnosticSeverity.Error, "MaxResults phải > 0."));
        }
    }

    private static void ValidateReviewParameterCompleteness(ReviewParameterCompletenessRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        RequireNonEmpty(request.RequiredParameterNames, "REQUIRED_PARAMETERS_EMPTY", "RequiredParameterNames phải có ít nhất 1 giá trị.", diagnostics);
    }

    private static void ValidateReviewRuleSetRun(ReviewRuleSetRunRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(request.RuleSetName))
        {
            diagnostics.Add(DiagnosticRecord.Create("RULE_SET_REQUIRED", DiagnosticSeverity.Error, "RuleSetName không được rỗng."));
        }

        if (request.MaxIssues <= 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("MAX_ISSUES_INVALID", DiagnosticSeverity.Error, "MaxIssues phải > 0."));
        }
    }

    private static void ValidateScheduleExtraction(ScheduleExtractionRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (request.ScheduleId <= 0 && string.IsNullOrWhiteSpace(request.ScheduleName))
        {
            diagnostics.Add(DiagnosticRecord.Create("SCHEDULE_SCOPE_REQUIRED", DiagnosticSeverity.Error, "Can ScheduleId hoac ScheduleName de extract schedule structured."));
        }

        if (request.MaxRows <= 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("SCHEDULE_MAX_ROWS_INVALID", DiagnosticSeverity.Error, "MaxRows phai > 0."));
        }
    }

    private static void ValidateSmartQc(SmartQcRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(request.RulesetName))
        {
            diagnostics.Add(DiagnosticRecord.Create("SMART_QC_RULESET_REQUIRED", DiagnosticSeverity.Error, "RulesetName khong duoc rong."));
        }

        if (request.MaxFindings <= 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("SMART_QC_MAX_FINDINGS_INVALID", DiagnosticSeverity.Error, "MaxFindings phai > 0."));
        }

        if (request.MaxSheets <= 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("SMART_QC_MAX_SHEETS_INVALID", DiagnosticSeverity.Error, "MaxSheets phai > 0."));
        }

        if (request.MaxNamingViolations <= 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("SMART_QC_MAX_NAMING_INVALID", DiagnosticSeverity.Error, "MaxNamingViolations phai > 0."));
        }

        if (double.IsNaN(request.DuplicateToleranceMm) || double.IsInfinity(request.DuplicateToleranceMm) || request.DuplicateToleranceMm <= 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("SMART_QC_DUPLICATE_TOLERANCE_INVALID", DiagnosticSeverity.Error, "DuplicateToleranceMm phai > 0."));
        }

        if (request.SheetIds != null && request.SheetIds.Any(x => x <= 0))
        {
            diagnostics.Add(DiagnosticRecord.Create("SMART_QC_SHEET_ID_INVALID", DiagnosticSeverity.Error, "SheetIds chi duoc chua SheetId > 0."));
        }
    }

    private static void ValidateFamilyXray(FamilyXrayRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (request.FamilyId <= 0 && string.IsNullOrWhiteSpace(request.FamilyName))
        {
            diagnostics.Add(DiagnosticRecord.Create("FAMILY_XRAY_SCOPE_REQUIRED", DiagnosticSeverity.Error, "Can FamilyId hoac FamilyName de xray family."));
        }

        if (request.MaxNestedFamilies <= 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("FAMILY_XRAY_MAX_NESTED_INVALID", DiagnosticSeverity.Error, "MaxNestedFamilies phai > 0."));
        }

        if (request.MaxParameters <= 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("FAMILY_XRAY_MAX_PARAMETERS_INVALID", DiagnosticSeverity.Error, "MaxParameters phai > 0."));
        }

        if (request.MaxTypeNames <= 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("FAMILY_XRAY_MAX_TYPES_INVALID", DiagnosticSeverity.Error, "MaxTypeNames phai > 0."));
        }
    }

    private static void ValidateSheetCaptureIntelligence(SheetCaptureIntelligenceRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (request.SheetId <= 0 && string.IsNullOrWhiteSpace(request.SheetNumber))
        {
            diagnostics.Add(DiagnosticRecord.Create("SHEET_INTELLIGENCE_SCOPE_REQUIRED", DiagnosticSeverity.Error, "Can SheetId hoac SheetNumber de capture intelligence."));
        }

        if (request.MaxViewports <= 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("SHEET_INTELLIGENCE_MAX_VIEWPORTS_INVALID", DiagnosticSeverity.Error, "MaxViewports phai > 0."));
        }

        if (request.MaxSchedules <= 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("SHEET_INTELLIGENCE_MAX_SCHEDULES_INVALID", DiagnosticSeverity.Error, "MaxSchedules phai > 0."));
        }

        if (request.MaxSheetTextNotes <= 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("SHEET_INTELLIGENCE_MAX_NOTES_INVALID", DiagnosticSeverity.Error, "MaxSheetTextNotes phai > 0."));
        }

        if (request.MaxViewportTextNotes <= 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("SHEET_INTELLIGENCE_MAX_VIEWPORT_NOTES_INVALID", DiagnosticSeverity.Error, "MaxViewportTextNotes phai > 0."));
        }
    }

    private static void ValidateSheetSummary(SheetSummaryRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (request.MaxPlacedViews <= 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("MAX_PLACED_VIEWS_INVALID", DiagnosticSeverity.Error, "MaxPlacedViews phải > 0."));
        }

        if (!request.SheetId.HasValue && string.IsNullOrWhiteSpace(request.SheetNumber) && string.IsNullOrWhiteSpace(request.SheetName))
        {
            diagnostics.Add(DiagnosticRecord.Create("SHEET_IDENTIFIER_REQUIRED", DiagnosticSeverity.Error, "Cần SheetId hoặc SheetNumber/SheetName."));
        }
    }

    private static void ValidateCaptureSnapshot(CaptureSnapshotRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (!IsAllowedValue(request.Scope, "active_view", "sheet", "selection", "elements"))
        {
            diagnostics.Add(DiagnosticRecord.Create("SNAPSHOT_SCOPE_INVALID", DiagnosticSeverity.Error, "Scope snapshot không hợp lệ."));
        }

        if (string.Equals(request.Scope, "sheet", System.StringComparison.OrdinalIgnoreCase) &&
            !request.SheetId.HasValue &&
            string.IsNullOrWhiteSpace(request.SheetNumber) &&
            string.IsNullOrWhiteSpace(request.SheetName))
        {
            diagnostics.Add(DiagnosticRecord.Create("SNAPSHOT_SHEET_REQUIRED", DiagnosticSeverity.Error, "Scope=sheet cần SheetId hoặc SheetNumber/SheetName."));
        }

        if (string.Equals(request.Scope, "elements", System.StringComparison.OrdinalIgnoreCase))
        {
            RequireNonEmpty(request.ElementIds, "SNAPSHOT_ELEMENT_IDS_EMPTY", "Scope=elements cần ElementIds.", diagnostics);
        }

        if (request.MaxElements <= 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("SNAPSHOT_MAX_ELEMENTS_INVALID", DiagnosticSeverity.Error, "MaxElements phải > 0."));
        }

        if (request.ExportImage && request.ImagePixelSize <= 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("SNAPSHOT_IMAGE_PIXEL_SIZE_INVALID", DiagnosticSeverity.Error, "ImagePixelSize phải > 0."));
        }
    }

    private static void ValidateTaskContext(TaskContextRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (request.MaxRecentOperations < 0 || request.MaxRecentEvents < 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("TASK_CONTEXT_LIMIT_INVALID", DiagnosticSeverity.Error, "MaxRecentOperations/MaxRecentEvents không được âm."));
        }
    }
}
