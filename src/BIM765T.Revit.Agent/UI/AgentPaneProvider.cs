using Autodesk.Revit.UI;
using BIM765T.Revit.Agent.Config;
using BIM765T.Revit.Agent.Infrastructure;

namespace BIM765T.Revit.Agent.UI;

internal sealed class AgentPaneProvider : IDockablePaneProvider
{
    private readonly AgentSettings _settings;

    internal AgentPaneProvider(AgentSettings settings)
    {
        _settings = settings;
    }

    public void SetupDockablePane(DockablePaneProviderData data)
    {
        var control = new AgentPaneControl(_settings);

        // Lưu static reference — cho phép tool responses push update lên UI
        AgentHost.PaneControl = control;

        data.FrameworkElement = control;
        data.InitialState = new DockablePaneState
        {
            DockPosition = DockPosition.Right
        };
    }
}
