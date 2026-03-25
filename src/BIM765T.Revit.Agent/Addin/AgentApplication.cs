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
                ToolTip = "Mo chat truc tiep voi 765T Assistant",
                LongDescription = "Mo pane chat-first de chat, preview, approve va review evidence ngay trong Revit."
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
                ToolTip = "Xem context document, view va selection hien tai",
                Image = RibbonIconFactory.CreateContextSmall(),
                LargeImage = RibbonIconFactory.CreateContextLarge()
            };

            var settingsBtn = new PushButtonData(
                "BIM765T.Settings",
                "Settings",
                assembly,
                typeof(CmdSettings).FullName)
            {
                ToolTip = "Cai dat 765T AI Bridge va external AI gateways",
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
                ToolTip = "Chay health check nhanh",
                LongDescription = "Chay document_health_v1 rule set. Ket qua hien trong Activity surface.",
                Image = RibbonIconFactory.CreateHealthSmall(),
                LargeImage = RibbonIconFactory.CreateHealthLarge()
            };

            var warningsBtn = new PushButtonData(
                "BIM765T.Warnings",
                "Warnings",
                assembly,
                typeof(CmdWarnings).FullName)
            {
                ToolTip = "Thu thap va phan loai tat ca Revit warnings",
                Image = RibbonIconFactory.CreateWarningsSmall(),
                LargeImage = RibbonIconFactory.CreateWarningsLarge()
            };

            var snapshotBtn = new PushButtonData(
                "BIM765T.Snapshot",
                "Snapshot",
                assembly,
                typeof(CmdSnapshot).FullName)
            {
                ToolTip = "Chup snapshot view hien tai (JSON + PNG)",
                Image = RibbonIconFactory.CreateSnapshotSmall(),
                LargeImage = RibbonIconFactory.CreateSnapshotLarge()
            };

            utilitiesPanel.AddItem(healthBtn);
            utilitiesPanel.AddStackedItems(warningsBtn, snapshotBtn);
        }
    }

    private static void RegisterPane(UIControlledApplication app)
    {
        if (!AgentHost.TryGetCurrent(out var runtime) || runtime == null)
        {
            throw new InvalidOperationException("Agent settings are not initialized.");
        }

        var provider = new AgentPaneProvider(runtime.Settings);
        app.RegisterDockablePane(new DockablePaneId(AgentHost.DockPaneGuid), "765T Assistant", provider);
    }
}
