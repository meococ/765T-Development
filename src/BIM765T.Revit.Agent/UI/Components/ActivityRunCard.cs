using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BIM765T.Revit.Agent.UI.Theme;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Agent.UI.Components;

internal sealed class ActivityRunCard : Border
{
    internal event Action<OperationJournalEntry>? Selected;

    internal ActivityRunCard(OperationJournalEntry entry)
    {
        Background = AppTheme.CardBackground;
        BorderBrush = AppTheme.SubtleBorder;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(AppTheme.CardRadius);
        Padding = new Thickness(AppTheme.SpaceMD);
        Margin = new Thickness(0, 0, 0, AppTheme.SpaceSM);
        Cursor = Cursors.Hand;

        var accent = entry.Succeeded ? AppTheme.Success : AppTheme.Danger;
        var stack = new StackPanel();
        var row = new DockPanel();
        row.Children.Add(new Border
        {
            Width = 24,
            Height = 24,
            CornerRadius = new CornerRadius(6),
            Background = entry.Succeeded ? AppTheme.CardGlowGreen : AppTheme.CardGlowAmber,
            Margin = new Thickness(0, 0, AppTheme.SpaceSM, 0),
            Child = IconLibrary.Create(entry.Succeeded ? IconLibrary.CheckMark : IconLibrary.Warning, 12, accent)
        });
        row.Children.Add(new TextBlock
        {
            Text = entry.ToolName,
            Foreground = AppTheme.TextPrimary,
            FontSize = AppTheme.FontBody,
            FontWeight = FontWeights.SemiBold
        });
        var badge = new StatusBadge(entry.Succeeded ? "OK" : "ERROR", accent);
        DockPanel.SetDock(badge, Dock.Right);
        row.Children.Add(badge);
        stack.Children.Add(row);
        stack.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(entry.ResultSummary) ? entry.StatusCode : entry.ResultSummary,
            Foreground = AppTheme.TextMuted,
            FontSize = AppTheme.FontCaption,
            FontWeight = AppTheme.WeightBody,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, AppTheme.SpaceXS, 0, AppTheme.SpaceXS)
        });
        stack.Children.Add(UIFactory.DetailRow("Time", entry.StartedUtc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture)));
        stack.Children.Add(UIFactory.DetailRow("Status", entry.StatusCode));
        if (entry.ChangedIds.Count > 0)
        {
            stack.Children.Add(UIFactory.DetailRow("Changed", entry.ChangedIds.Count.ToString(CultureInfo.InvariantCulture)));
        }

        Child = stack;
        MouseEnter += (_, _) =>
        {
            Background = AppTheme.SurfaceMuted;
            BorderBrush = AppTheme.FrozenAlpha(accent.Color.ToString(CultureInfo.InvariantCulture), 0.35);
        };
        MouseLeave += (_, _) =>
        {
            Background = AppTheme.CardBackground;
            BorderBrush = AppTheme.SubtleBorder;
        };
        MouseLeftButtonUp += (_, _) => Selected?.Invoke(entry);
    }
}
