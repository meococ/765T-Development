using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BIM765T.Revit.Agent.UI.Theme;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Agent.UI.Components;

internal sealed class EvidenceSummaryCard : Border
{
    internal event Action<WorkerEvidenceItem>? Selected;

    internal EvidenceSummaryCard(WorkerEvidenceItem item)
    {
        Background = AppTheme.ResponseCardBg;
        BorderBrush = AppTheme.ResponseCardBorder;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(AppTheme.CardRadius);
        Padding = new Thickness(AppTheme.SpaceMD);
        Margin = new Thickness(0, 0, 0, AppTheme.SpaceSM);
        Cursor = Cursors.Hand;

        var stack = new StackPanel();
        var row = new DockPanel();
        row.Children.Add(new Border
        {
            Width = 24,
            Height = 24,
            CornerRadius = new CornerRadius(6),
            Background = item.Verified ? AppTheme.CardGlowGreen : AppTheme.CardGlowBlue,
            Margin = new Thickness(0, 0, AppTheme.SpaceSM, 0),
            Child = IconLibrary.Create(item.Verified ? IconLibrary.CheckMark : IconLibrary.Evidence, 12, item.Verified ? AppTheme.Success : AppTheme.AccentBlue)
        });
        row.Children.Add(new TextBlock
        {
            Text = item.Title,
            Foreground = AppTheme.TextPrimary,
            FontSize = AppTheme.FontBody,
            FontWeight = FontWeights.SemiBold
        });
        var badge = new StatusBadge(item.Verified ? "VERIFIED" : "ARTIFACT", item.Verified ? AppTheme.Success : AppTheme.AccentBlue);
        DockPanel.SetDock(badge, Dock.Right);
        row.Children.Add(badge);
        stack.Children.Add(row);
        stack.Children.Add(new TextBlock
        {
            Text = item.Summary,
            Foreground = AppTheme.TextMuted,
            FontSize = AppTheme.FontCaption,
            FontWeight = AppTheme.WeightBody,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, AppTheme.SpaceXS, 0, AppTheme.SpaceXS)
        });

        var facts = new List<KeyValuePair<string, string>>
        {
            new KeyValuePair<string, string>("Source", string.IsNullOrWhiteSpace(item.SourceToolName) ? "-" : item.SourceToolName),
            new KeyValuePair<string, string>("Verify", string.IsNullOrWhiteSpace(item.VerificationMode) ? "-" : item.VerificationMode)
        };
        foreach (var fact in facts)
        {
            stack.Children.Add(UIFactory.DetailRow(fact.Key, fact.Value));
        }

        Child = stack;
        MouseEnter += (_, _) =>
        {
            Background = AppTheme.SurfaceMuted;
            BorderBrush = item.Verified ? AppTheme.FrozenAlpha(AppTheme.Success.Color.ToString(CultureInfo.InvariantCulture), 0.35) : AppTheme.FrozenAlpha(AppTheme.AccentBlue.Color.ToString(CultureInfo.InvariantCulture), 0.35);
        };
        MouseLeave += (_, _) =>
        {
            Background = AppTheme.ResponseCardBg;
            BorderBrush = AppTheme.ResponseCardBorder;
        };
        MouseLeftButtonUp += (_, _) => Selected?.Invoke(item);
    }
}
