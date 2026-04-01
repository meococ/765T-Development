using System;
using Autodesk.Revit.UI;
using BIM765T.Revit.Agent.Addin.Commands;
using BIM765T.Revit.Agent.Infrastructure;
using BIM765T.Revit.Agent.UI;

namespace BIM765T.Revit.Agent.Addin;

public sealed class AgentApplication : IExternalApplication
{
    private const string TabName = "765T AI";
    private const string AssistantPanel = "Assistant";
    private const string UtilitiesPanel = "Utilities";

    public Result OnStartup(UIControlledApplication application)
    {
        try
        {
            AgentHost.Initialize(application);
            RegisterRibbon(application);
            RegisterPane(application);
            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            TaskDialog.Show("765T Revit Bridge", "Startup failed:\n" + ex);
            return Result.Failed;
        }
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        AgentHost.Shutdown();
        return Result.Succeeded;
    }

    private static void RegisterRibbon(UIControlledApplication app)
    {
        try { app.CreateRibbonTab(TabName); } catch { }

        var assembly = typeof(AgentApplication).Assembly.Location;

        RibbonPanel? assistantPanel = null;
        try { assistantPanel = app.CreateRibbonPanel(TabName, AssistantPanel); } catch { }

        if (assistantPanel != null)
        {
            var chatBtn = new PushButtonData(
                "BIM765T.Chat",
                "Chat",
                assembly,
                typeof(CmdShowAgentPane).FullName)
            {
                ToolTip = "Open chat with 765T Assistant",
                LongDescription = "Open the chat-first pane to chat, preview, approve, and review evidence directly in Revit."
            };
            chatBtn.Image = RibbonIconFactory.CreateAgentSmall();
            chatBtn.LargeImage = RibbonIconFactory.CreateAgentLarge();
            assistantPanel.AddItem(chatBtn);

            var contextBtn = new PushButtonData(
                "BIM765T.Context",
                "Context",
                assembly,
                typeof(CmdCurrentContext).FullName)
            {
                ToolTip = "View current document, view, and selection context",
                Image = RibbonIconFactory.CreateContextSmall(),
                LargeImage = RibbonIconFactory.CreateContextLarge()
            };

            var settingsBtn = new PushButtonData(
                "BIM765T.Settings",
                "Settings",
                assembly,
                typeof(CmdSettings).FullName)
            {
                ToolTip = "Configure 765T AI Bridge and external AI gateways",
                Image = RibbonIconFactory.CreateSettingsSmall(),
                LargeImage = RibbonIconFactory.CreateSettingsLarge()
            };

            assistantPanel.AddStackedItems(contextBtn, settingsBtn);
        }

        RibbonPanel? utilitiesPanel = null;
        try { utilitiesPanel = app.CreateRibbonPanel(TabName, UtilitiesPanel); } catch { }

        if (utilitiesPanel != null)
        {
            var healthBtn = new PushButtonData(
                "BIM765T.Health",
                "Health\nCheck",
                assembly,
                typeof(CmdHealthCheck).FullName)
            {
                ToolTip = "Run a quick health check",
                LongDescription = "Run the document_health_v1 rule set. Results shown in the Activity surface.",
                Image = RibbonIconFactory.CreateHealthSmall(),
                LargeImage = RibbonIconFactory.CreateHealthLarge()
            };

            var warningsBtn = new PushButtonData(
                "BIM765T.Warnings",
                "Warnings",
                assembly,
                typeof(CmdWarnings).FullName)
            {
                ToolTip = "Collect and categorize all Revit warnings",
                Image = RibbonIconFactory.CreateWarningsSmall(),
                LargeImage = RibbonIconFactory.CreateWarningsLarge()
            };

            var snapshotBtn = new PushButtonData(
                "BIM765T.Snapshot",
                "Snapshot",
                assembly,
                typeof(CmdSnapshot).FullName)
            {
                ToolTip = "Capture snapshot of the current view (JSON + PNG)",
                Image = RibbonIconFactory.CreateSnapshotSmall(),
                LargeImage = RibbonIconFactory.CreateSnapshotLarge()
            };

            var filterAuditBtn = new PushButtonData(
                "BIM765T.FilterAudit",
                "Filter\nAudit",
                assembly,
                typeof(CmdExportFilterAudit).FullName)
            {
                ToolTip = "Export current document/view filter audit to JSON",
                LongDescription = "Dump all ParameterFilterElements plus active-view/template usage to a JSON file for external analysis.",
                Image = RibbonIconFactory.CreateContextSmall(),
                LargeImage = RibbonIconFactory.CreateContextLarge()
            };

            utilitiesPanel.AddItem(healthBtn);
            utilitiesPanel.AddStackedItems(warningsBtn, snapshotBtn, filterAuditBtn);
        }
    }

    private static void RegisterPane(UIControlledApplication app)
    {
        if (!AgentHost.TryGetCurrent(out var runtime) || runtime == null)
        {
            throw new InvalidOperationException("Agent settings are not initialized.");
        }

        if (!runtime.Settings.EnableUiPane)
        {
            return;
        }

        var provider = new AgentPaneProvider(runtime.Settings);
        app.RegisterDockablePane(new DockablePaneId(AgentHost.DockPaneGuid), "765T Assistant", provider);
    }
}
