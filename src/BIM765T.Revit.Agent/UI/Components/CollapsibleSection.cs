using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BIM765T.Revit.Agent.UI.Theme;

namespace BIM765T.Revit.Agent.UI.Components;

/// <summary>
/// Accordion/collapsible section — header click toggles expand/collapse.
/// Header: icon ▶/▼ + title + item count badge.
/// Content: StackPanel children, animated MaxHeight transition.
/// </summary>
internal sealed class CollapsibleSection : Border
{
    private readonly TextBlock _arrow;
    private readonly StackPanel _contentPanel;
    private readonly Border _contentWrapper;
    private bool _isExpanded;

    /// <summary>The inner panel where child elements should be added.</summary>
    internal StackPanel Content => _contentPanel;

    internal CollapsibleSection(string title, int itemCount = 0, bool defaultExpanded = true, SolidColorBrush? accentColor = null)
    {
        Margin = new Thickness(0, 0, 0, AppTheme.SpaceSM);

        var root = new StackPanel();

        // ── Header (clickable) ──
        var header = new Border
        {
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            Padding = new Thickness(0, AppTheme.SpaceSM, 0, AppTheme.SpaceSM)
        };

        var headerDock = new DockPanel();

        // Arrow indicator
        _arrow = new TextBlock
        {
            Text = defaultExpanded ? IconLibrary.ChevronDown : IconLibrary.ChevronRight,
            FontFamily = AppTheme.FontIcon,
            FontSize = 10,
            Foreground = AppTheme.TextMuted,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, AppTheme.SpaceSM, 0),
            Width = 14
        };
        DockPanel.SetDock(_arrow, Dock.Left);
        headerDock.Children.Add(_arrow);

        // Accent bar (optional)
        if (accentColor != null)
        {
            var bar = new Border
            {
                Width = 3,
                Height = 14,
                Background = accentColor,
                CornerRadius = new CornerRadius(2),
                Margin = new Thickness(0, 0, AppTheme.SpaceSM, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(bar, Dock.Left);
            headerDock.Children.Add(bar);
        }

        // Item count badge (right side)
        if (itemCount > 0)
        {
            var badge = new Border
            {
                Background = AppTheme.SurfaceElevated,
                CornerRadius = new CornerRadius(AppTheme.SpaceXS),
                Padding = new Thickness(6, 1, 6, 1),
                Margin = new Thickness(AppTheme.SpaceSM, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = itemCount.ToString(CultureInfo.InvariantCulture),
                    Foreground = AppTheme.TextMuted,
                    FontSize = AppTheme.FontCaption,
                    FontWeight = FontWeights.SemiBold
                }
            };
            DockPanel.SetDock(badge, Dock.Right);
            headerDock.Children.Add(badge);
        }

        // Title
        headerDock.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = AppTheme.TextPrimary,
            FontSize = AppTheme.FontBody,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });

        header.Child = headerDock;

        // Divider
        var divider = new Border
        {
            Height = 1,
            Background = AppTheme.CardBorder,
            Margin = new Thickness(0, 0, 0, AppTheme.SpaceXS)
        };

        // ── Content ──
        _contentPanel = new StackPanel();
        _contentWrapper = new Border
        {
            Child = _contentPanel,
            ClipToBounds = true
        };

        root.Children.Add(header);
        root.Children.Add(divider);
        root.Children.Add(_contentWrapper);

        Child = root;

        // ── State ──
        _isExpanded = defaultExpanded;
        if (!defaultExpanded)
        {
            _contentWrapper.Visibility = Visibility.Collapsed;
        }

        // ── Click to toggle ──
        header.MouseLeftButtonUp += (_, __) => Toggle();
        header.MouseEnter += (_, __) => header.Background = new SolidColorBrush(AppTheme.HoverBg.Color);
        header.MouseLeave += (_, __) => header.Background = Brushes.Transparent;
    }

    /// <summary>Toggle expand/collapse state.</summary>
    internal void Toggle()
    {
        _isExpanded = !_isExpanded;

        if (_isExpanded)
        {
            _arrow.Text = IconLibrary.ChevronDown;
            _contentWrapper.Visibility = Visibility.Visible;
            AnimationHelper.FadeIn(_contentWrapper, AppTheme.AnimFast);
        }
        else
        {
            _arrow.Text = IconLibrary.ChevronRight;
            AnimationHelper.FadeOut(_contentWrapper, AppTheme.AnimFast);
        }
    }

    /// <summary>Programmatically set expanded state.</summary>
    internal void SetExpanded(bool expanded)
    {
        if (_isExpanded == expanded) return;
        Toggle();
    }
}
