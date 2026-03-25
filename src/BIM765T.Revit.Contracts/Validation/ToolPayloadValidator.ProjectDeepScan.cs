using System.Collections.Generic;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Contracts.Validation;

public static partial class ToolPayloadValidator
{
    private static void ValidateProjectDeepScan(ProjectDeepScanRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(request.WorkspaceId))
        {
            diagnostics.Add(DiagnosticRecord.Create("PROJECT_WORKSPACE_REQUIRED", DiagnosticSeverity.Error, "WorkspaceId khong duoc rong."));
        }

        if (request.MaxDocuments <= 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("PROJECT_DEEP_SCAN_MAX_DOCUMENTS_INVALID", DiagnosticSeverity.Error, "MaxDocuments phai > 0."));
        }

        if (request.MaxSheets <= 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("PROJECT_DEEP_SCAN_MAX_SHEETS_INVALID", DiagnosticSeverity.Error, "MaxSheets phai > 0."));
        }

        if (request.MaxSheetIntelligence <= 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("PROJECT_DEEP_SCAN_MAX_SHEET_INTELLIGENCE_INVALID", DiagnosticSeverity.Error, "MaxSheetIntelligence phai > 0."));
        }

        if (request.MaxSchedulesPerSheet <= 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("PROJECT_DEEP_SCAN_MAX_SCHEDULES_INVALID", DiagnosticSeverity.Error, "MaxSchedulesPerSheet phai > 0."));
        }

        if (request.MaxScheduleRows <= 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("PROJECT_DEEP_SCAN_MAX_SCHEDULE_ROWS_INVALID", DiagnosticSeverity.Error, "MaxScheduleRows phai > 0."));
        }

        if (request.MaxFindings <= 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("PROJECT_DEEP_SCAN_MAX_FINDINGS_INVALID", DiagnosticSeverity.Error, "MaxFindings phai > 0."));
        }
    }

    private static void ValidateProjectDeepScanGet(ProjectDeepScanGetRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(request.WorkspaceId))
        {
            diagnostics.Add(DiagnosticRecord.Create("PROJECT_WORKSPACE_REQUIRED", DiagnosticSeverity.Error, "WorkspaceId khong duoc rong."));
        }
    }
}
