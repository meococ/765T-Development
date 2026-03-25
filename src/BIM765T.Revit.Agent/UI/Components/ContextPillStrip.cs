using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using BIM765T.Revit.Agent.UI.Theme;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Agent.UI.Components;

internal sealed class ContextPillStrip : Border
{
    private readonly WrapPanel _panel;

    internal ContextPillStrip()
    {
        Background = AppTheme.PageBackground;
        BorderBrush = AppTheme.SubtleBorder;
        BorderThickness = new Thickness(0, 0, 0, 1);
        Padding = new Thickness(AppTheme.SpaceLG, AppTheme.SpaceXS, AppTheme.SpaceLG, AppTheme.SpaceSM);

        _panel = new WrapPanel();
        Child = _panel;
    }

    internal void SetItems(IEnumerable<WorkerContextPill> items)
    {
        _panel.Children.Clear();
        foreach (var item in items ?? new List<WorkerContextPill>())
        {
            var accent = ResolveTone(item.Tone);
            var pill = new Border
            {
                Background = item.IsPrimary ? AppTheme.SurfaceMuted : AppTheme.PillBackground,
                BorderBrush = AppTheme.FrozenAlpha(accent.Color.ToString(CultureInfo.InvariantCulture), item.IsPrimary ? 0.40 : 0.20),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(AppTheme.BadgeRadius),
                Margin = new Thickness(0, 0, AppTheme.SpaceSM, AppTheme.SpaceXS),
                Padding = new Thickness(AppTheme.SpaceSM, 3, AppTheme.SpaceSM, 3),
                ToolTip = string.IsNullOrWhiteSpace(item.Tooltip) ? null : item.Tooltip,
                MaxWidth = item.IsPrimary ? 300 : 260
            };

            var row = new DockPanel { LastChildFill = true };
            var iconGlyph = ResolveIcon(item.Icon, item.Key);
            if (!string.IsNullOrWhiteSpace(iconGlyph))
            {
                var icon = IconLibrary.Create(iconGlyph, 11, accent);
                icon.Margin = new Thickness(0, 0, 6, 0);
                DockPanel.SetDock(icon, Dock.Left);
                row.Children.Add(icon);
            }

            var label = new TextBlock
            {
                Text = item.Label + ": ",
                Foreground = AppTheme.TextMuted,
                FontSize = AppTheme.FontCaption,
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(label, Dock.Left);
            row.Children.Add(label);

            row.Children.Add(new TextBlock
            {
                Text = item.Value,
                Foreground = accent,
                FontSize = AppTheme.FontCaption,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            });

            pill.Child = row;
            _panel.Children.Add(pill);
        }
    }

    private static string ResolveIcon(string icon, string key)
    {
        var token = string.IsNullOrWhiteSpace(icon) ? key : icon;
        switch ((token ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "document":
                return IconLibrary.Document;
            case "view":
                return IconLibrary.View;
            case "selection":
                return IconLibrary.Element;
            case "workspace":
                return IconLibrary.Flow;
            case "mission":
            case "capability":
                return IconLibrary.WorkerChat;
            case "safety":
                return IconLibrary.Shield;
            default:
                return IconLibrary.Context;
        }
    }

    private static System.Windows.Media.SolidColorBrush ResolveTone(string tone)
    {
        switch ((tone ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "success": return AppTheme.Success;
            case "warning": return AppTheme.Warning;
            case "danger": return AppTheme.Danger;
            case "info": return AppTheme.Info;
            case "accent": return AppTheme.AccentBlue;
            default: return AppTheme.TextSecondary;
        }
    }
}
