using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BIM765T.Revit.Agent.Infrastructure;

namespace BIM765T.Revit.Agent.Addin.Commands;

[Transaction(TransactionMode.Manual)]
public sealed class CmdShowAgentPane : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        try
        {
            var pane = commandData.Application.GetDockablePane(new DockablePaneId(AgentHost.DockPaneGuid));
            pane?.Show();
            return Result.Succeeded;
        }
        catch
        {
            TaskDialog.Show("765T Assistant", "The dockable pane is not registered. EnableUiPane must be true in the local settings for the in-Revit shell.");
            return Result.Cancelled;
        }
    }
}
