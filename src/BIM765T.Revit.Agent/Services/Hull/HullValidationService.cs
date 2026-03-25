using System.Linq;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Hull;

namespace BIM765T.Revit.Agent.Services.Hull;

internal sealed class HullValidationService
{
    internal void Enrich(HullDryRunResponse response)
    {
        var duplicateTraceKeys = response.Actions
            .Where(x => !string.IsNullOrWhiteSpace(x.TraceKey))
            .GroupBy(x => x.TraceKey)
            .Where(g => g.Count() > 1)
            .ToList();

        foreach (var group in duplicateTraceKeys)
        {
            foreach (var action in group)
            {
                action.Diagnostics.Add(DiagnosticRecord.Create("DUPLICATE_TRACE_KEY", DiagnosticSeverity.Warning, "Trace key trùng trong dry-run planner: " + group.Key, action.SourceId));
            }
        }

        if (duplicateTraceKeys.Count > 0)
        {
            response.Diagnostics.Add(DiagnosticRecord.Create("VALIDATION_DUPLICATE_TRACE", DiagnosticSeverity.Warning, "Dry-run phát hiện trace key trùng, cần xử lý trước khi mở write tools."));
        }

        if (response.Actions.Count == 0)
        {
            response.Diagnostics.Add(DiagnosticRecord.Create("EMPTY_PLAN", DiagnosticSeverity.Info, "Không có planned action nào trong active view."));
        }
    }
}
