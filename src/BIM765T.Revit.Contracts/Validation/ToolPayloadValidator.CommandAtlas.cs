using System.Collections.Generic;
using System.Linq;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Contracts.Validation;

public static partial class ToolPayloadValidator
{
    private static void ValidateCommandAtlasSearch(CommandAtlasSearchRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (request.MaxResults <= 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("COMMAND_SEARCH_MAX_RESULTS_INVALID", DiagnosticSeverity.Error, "MaxResults phai > 0."));
        }
    }

    private static void ValidateCommandDescribe(CommandDescribeRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(request.CommandId) && string.IsNullOrWhiteSpace(request.Query))
        {
            diagnostics.Add(DiagnosticRecord.Create("COMMAND_DESCRIBE_TARGET_REQUIRED", DiagnosticSeverity.Error, "CommandId hoac Query phai co it nhat 1 gia tri."));
        }
    }

    private static void ValidateCommandExecute(CommandExecuteRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(request.CommandId) && string.IsNullOrWhiteSpace(request.Query))
        {
            diagnostics.Add(DiagnosticRecord.Create("COMMAND_EXECUTE_TARGET_REQUIRED", DiagnosticSeverity.Error, "CommandId hoac Query phai co it nhat 1 gia tri."));
        }
    }

    private static void ValidateCoverageReport(CoverageReportRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (!string.IsNullOrWhiteSpace(request.CoverageTier)
            && !IsAllowedValue(request.CoverageTier, CommandCoverageTiers.Baseline, CommandCoverageTiers.Extended, CommandCoverageTiers.Experimental))
        {
            diagnostics.Add(DiagnosticRecord.Create("COMMAND_COVERAGE_TIER_INVALID", DiagnosticSeverity.Error, "CoverageTier khong hop le."));
        }
    }

    private static void ValidateQuickAction(QuickActionRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            diagnostics.Add(DiagnosticRecord.Create("QUICK_ACTION_QUERY_REQUIRED", DiagnosticSeverity.Error, "Query khong duoc rong."));
        }
    }

    private static void ValidateFallbackArtifactRequest(FallbackArtifactRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            diagnostics.Add(DiagnosticRecord.Create("FALLBACK_ARTIFACT_QUERY_REQUIRED", DiagnosticSeverity.Error, "Query khong duoc rong."));
        }

        var allowedKinds = new[]
        {
            FallbackArtifactKinds.Playbook,
            FallbackArtifactKinds.CsvMapping,
            FallbackArtifactKinds.OpenXmlRecipe,
            FallbackArtifactKinds.ExportProfile,
            FallbackArtifactKinds.DynamoTemplate,
            FallbackArtifactKinds.ExternalWrapper
        };

        foreach (var kind in request.RequestedKinds ?? Enumerable.Empty<string>())
        {
            if (!string.IsNullOrWhiteSpace(kind) && !allowedKinds.Contains(kind, System.StringComparer.OrdinalIgnoreCase))
            {
                diagnostics.Add(DiagnosticRecord.Create("FALLBACK_ARTIFACT_KIND_INVALID", DiagnosticSeverity.Error, $"Fallback artifact kind `{kind}` khong hop le."));
            }
        }
    }

    private static void ValidateMemoryScopedSearch(MemoryScopedSearchRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (request.MaxResults <= 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("MEMORY_SCOPED_MAX_RESULTS_INVALID", DiagnosticSeverity.Error, "MaxResults phai > 0."));
        }

        if (!IsAllowedValue(request.RetrievalScope, RetrievalScopes.QuickPath, RetrievalScopes.WorkflowPath, RetrievalScopes.DeliveryPath))
        {
            diagnostics.Add(DiagnosticRecord.Create("MEMORY_SCOPE_INVALID", DiagnosticSeverity.Error, "RetrievalScope khong hop le."));
        }
    }
}
