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
                action.Diagnostics.Add(DiagnosticRecord.Create("DUPLICATE_TRACE_KEY", DiagnosticSeverity.Warning, "Duplicate trace key in dry-run planner: " + group.Key, action.SourceId));
            }
        }

        if (duplicateTraceKeys.Count > 0)
        {
            response.Diagnostics.Add(DiagnosticRecord.Create("VALIDATION_DUPLICATE_TRACE", DiagnosticSeverity.Warning, "Dry-run detected duplicate trace keys; resolve before enabling write tools."));
        }

        if (response.Actions.Count == 0)
        {
            response.Diagnostics.Add(DiagnosticRecord.Create("EMPTY_PLAN", DiagnosticSeverity.Info, "No planned actions in the active view."));
        }
    }
}
