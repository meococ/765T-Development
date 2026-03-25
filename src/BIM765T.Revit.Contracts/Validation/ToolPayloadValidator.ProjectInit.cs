using System.Collections.Generic;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Contracts.Validation;

public static partial class ToolPayloadValidator
{
    private static void ValidateProjectInitPreview(ProjectInitPreviewRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(request.SourceRootPath))
        {
            diagnostics.Add(DiagnosticRecord.Create("PROJECT_SOURCE_ROOT_REQUIRED", DiagnosticSeverity.Error, "SourceRootPath khong duoc rong."));
        }
    }

    private static void ValidateProjectInitApply(ProjectInitApplyRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(request.SourceRootPath))
        {
            diagnostics.Add(DiagnosticRecord.Create("PROJECT_SOURCE_ROOT_REQUIRED", DiagnosticSeverity.Error, "SourceRootPath khong duoc rong."));
        }
    }

    private static void ValidateProjectManifest(ProjectManifestRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(request.WorkspaceId))
        {
            diagnostics.Add(DiagnosticRecord.Create("PROJECT_WORKSPACE_REQUIRED", DiagnosticSeverity.Error, "WorkspaceId khong duoc rong."));
        }
    }

    private static void ValidateProjectContextBundle(ProjectContextBundleRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(request.WorkspaceId))
        {
            diagnostics.Add(DiagnosticRecord.Create("PROJECT_WORKSPACE_REQUIRED", DiagnosticSeverity.Error, "WorkspaceId khong duoc rong."));
        }

        if (request.MaxSourceRefs <= 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("PROJECT_MAX_SOURCE_REFS_INVALID", DiagnosticSeverity.Error, "MaxSourceRefs phai > 0."));
        }

        if (request.MaxStandardsRefs <= 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("PROJECT_MAX_STANDARDS_REFS_INVALID", DiagnosticSeverity.Error, "MaxStandardsRefs phai > 0."));
        }
    }
}
