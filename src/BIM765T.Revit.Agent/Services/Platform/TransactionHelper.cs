using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using BIM765T.Revit.Agent.Infrastructure.Failures;
using BIM765T.Revit.Contracts.Common;

namespace BIM765T.Revit.Agent.Services.Platform;

/// <summary>
/// Helper to eliminate transaction boilerplate.
/// Wraps TransactionGroup + Transaction + AgentFailureHandling + error check.
/// </summary>
internal static class TransactionHelper
{
    /// <summary>
    /// Run action inside TransactionGroup -> Transaction with failure handling.
    /// Returns diagnostics collected during transaction.
    /// If any diagnostic has Error severity, group is rolled back.
    /// </summary>
    internal static List<DiagnosticRecord> RunSafe(
        Document doc,
        string operationName,
        Action<Transaction, List<DiagnosticRecord>> action)
    {
        var diagnostics = new List<DiagnosticRecord>();
        var groupName = $"BIM765T.Revit.Agent::{operationName}";
        var txName = operationName.Replace(".", " ").Replace("_", " ");

        using var group = new TransactionGroup(doc, groupName);
        group.Start();
        using var transaction = new Transaction(doc, txName);
        transaction.Start();
        AgentFailureHandling.Configure(transaction, diagnostics);

        try
        {
            action(transaction, diagnostics);
            doc.Regenerate();
            transaction.Commit();
        }
        catch (Exception ex)
        {
            diagnostics.Add(DiagnosticRecord.Create(
                "TRANSACTION_EXCEPTION",
                DiagnosticSeverity.Error,
                ex.Message));
            if (transaction.HasStarted()) transaction.RollBack();
        }

        if (diagnostics.Exists(d => d.Severity == DiagnosticSeverity.Error))
            group.RollBack();
        else
            group.Assimilate();

        return diagnostics;
    }
}
