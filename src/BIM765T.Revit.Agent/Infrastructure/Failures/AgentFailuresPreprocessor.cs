using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using BIM765T.Revit.Contracts.Common;

namespace BIM765T.Revit.Agent.Infrastructure.Failures;

internal sealed class AgentFailuresPreprocessor : IFailuresPreprocessor
{
    private readonly List<DiagnosticRecord> _diagnostics;

    internal AgentFailuresPreprocessor(List<DiagnosticRecord> diagnostics)
    {
        _diagnostics = diagnostics;
    }

    public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
    {
        var messages = failuresAccessor.GetFailureMessages().ToList();
        var hasBlockingFailure = false;

        foreach (var message in messages)
        {
            var text = message.GetDescriptionText();
            var severity = message.GetSeverity();
            var diagnosticSeverity = severity == FailureSeverity.Warning ? DiagnosticSeverity.Warning : DiagnosticSeverity.Error;
            _diagnostics.Add(DiagnosticRecord.Create("REVIT_FAILURE", diagnosticSeverity, $"[{severity}] {text}"));

            if (severity == FailureSeverity.Warning)
            {
                try
                {
                    failuresAccessor.DeleteWarning(message);
                }
                catch (Autodesk.Revit.Exceptions.ArgumentException ex)
                {
                    _diagnostics.Add(DiagnosticRecord.Create("REVIT_WARNING_DELETE_FAILED", DiagnosticSeverity.Warning, "Could not delete warning: " + ex.Message));
                }
                catch (Autodesk.Revit.Exceptions.InvalidOperationException ex)
                {
                    _diagnostics.Add(DiagnosticRecord.Create("REVIT_WARNING_DELETE_FAILED", DiagnosticSeverity.Warning, "Could not delete warning: " + ex.Message));
                }

                continue;
            }

            hasBlockingFailure = true;
        }

        if (hasBlockingFailure)
        {
            _diagnostics.Add(DiagnosticRecord.Create("REVIT_FAILURE_ROLLBACK", DiagnosticSeverity.Error, "Blocking Revit failure detected. Operation will rollback to avoid modal dialog waits."));
            return FailureProcessingResult.ProceedWithRollBack;
        }

        return FailureProcessingResult.Continue;
    }
}
