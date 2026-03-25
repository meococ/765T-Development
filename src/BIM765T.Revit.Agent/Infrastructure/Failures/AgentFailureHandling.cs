using System.Collections.Generic;
using Autodesk.Revit.DB;
using BIM765T.Revit.Contracts.Common;

namespace BIM765T.Revit.Agent.Infrastructure.Failures;

internal static class AgentFailureHandling
{
    internal static void Configure(Transaction transaction, List<DiagnosticRecord> diagnostics)
    {
        var options = transaction.GetFailureHandlingOptions();
        options.SetFailuresPreprocessor(new AgentFailuresPreprocessor(diagnostics));
        options.SetClearAfterRollback(true);
        transaction.SetFailureHandlingOptions(options);
    }
}
