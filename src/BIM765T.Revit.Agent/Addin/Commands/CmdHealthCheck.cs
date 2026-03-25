using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BIM765T.Revit.Agent.Infrastructure;
using BIM765T.Revit.Agent.Infrastructure.Bridge;
using BIM765T.Revit.Contracts.Bridge;

namespace BIM765T.Revit.Agent.Addin.Commands;

/// <summary>
/// Quick ribbon command: run Model Health Check directly from ribbon.
/// Gọi review.run_rule_set(document_health_v1) qua ExternalEvent pipeline.
/// </summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdHealthCheck : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        if (!AgentHost.TryGetCurrent(out var runtime) || runtime == null)
        {
            TaskDialog.Show("765T", "Agent chưa sẵn sàng.");
            return Result.Failed;
        }

        var request = new ToolRequestEnvelope
        {
            RequestId = System.Guid.NewGuid().ToString("N"),
            ToolName = ToolNames.ReviewRunRuleSet,
            PayloadJson = "{\"RuleSetName\":\"document_health_v1\"}",
            Caller = "Ribbon",
            SessionId = "ribbon-session",
            DryRun = false,
            CorrelationId = System.Guid.NewGuid().ToString("N")
        };

        runtime.Queue.Enqueue(new PendingToolInvocation(request));
        runtime.ExternalEvent.Raise();

        // Show pane on Activity tab to see results
        ShowPaneActivity(commandData.Application);
        return Result.Succeeded;
    }

    private static void ShowPaneActivity(UIApplication app)
    {
        try
        {
            var pane = app.GetDockablePane(new DockablePaneId(AgentHost.DockPaneGuid));
            pane?.Show();
            AgentHost.PaneControl?.SwitchToTab(5); // Activity tab
        }
        catch { /* pane may not be ready */ }
    }
}
