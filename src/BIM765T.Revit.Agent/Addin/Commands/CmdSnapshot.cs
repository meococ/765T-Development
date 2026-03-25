using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BIM765T.Revit.Agent.Infrastructure;
using BIM765T.Revit.Agent.Infrastructure.Bridge;
using BIM765T.Revit.Contracts.Bridge;

namespace BIM765T.Revit.Agent.Addin.Commands;

/// <summary>
/// Quick ribbon command: capture snapshot of active view.
/// </summary>
[Transaction(TransactionMode.Manual)]
public sealed class CmdSnapshot : IExternalCommand
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
            ToolName = "review.capture_snapshot",
            PayloadJson = "{\"Scope\":\"active_view\"}",
            Caller = "Ribbon",
            SessionId = "ribbon-session",
            DryRun = false,
            CorrelationId = System.Guid.NewGuid().ToString("N")
        };

        runtime.Queue.Enqueue(new PendingToolInvocation(request));
        runtime.ExternalEvent.Raise();

        try
        {
            var pane = commandData.Application.GetDockablePane(new DockablePaneId(AgentHost.DockPaneGuid));
            pane?.Show();
            AgentHost.PaneControl?.SwitchToTab(5);
        }
        catch { }

        return Result.Succeeded;
    }
}
