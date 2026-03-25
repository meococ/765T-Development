using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using BIM765T.Revit.Agent.UI.Theme;

namespace BIM765T.Revit.Agent.UI.Components;

/// <summary>
/// Toast notification v2 - slide-in/out animated, max 3 visible, X close button.
/// Auto-dismiss after 4s. Oldest dismissed first when >3.
/// </summary>
internal sealed class ToastNotification : Border
{
    private readonly DispatcherTimer _timer;
    private readonly Panel _parent;
    private readonly ToastType _type;
    private readonly TextBlock _closeButton;
    private readonly TextBlock _iconBlock;
    private readonly TextBlock _messageBlock;

    internal static void Show(Panel parent, string message, ToastType type)
    {
        var existing = parent.Children.OfType<ToastNotification>().ToList();
        while (existing.Count >= AppTheme.MaxToasts)
        {
            existing[0].DismissAnimated();
            existing.RemoveAt(0);
        }

        var toast = new ToastNotification(parent, message, type);
        parent.Children.Add(toast);
        AnimationHelper.SlideIn(toast, -20, AppTheme.AnimNormal);
    }

    internal static void ApplyThemeRefresh(Panel parent)
    {
        foreach (var toast in parent.Children.OfType<ToastNotification>())
        {
            toast.ApplyThemeRefresh();
        }
    }

    private ToastNotification(Panel parent, string message, ToastType type)
    {
        _parent = parent;
        _type = type;

        var (bg, border, icon) = ResolveThemePalette(type);

        Background = bg;
        BorderBrush = border;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(AppTheme.CardRadius);
        Padding = new Thickness(AppTheme.SpaceMD, AppTheme.SpaceSM + 2, AppTheme.SpaceSM, AppTheme.SpaceSM + 2);
        Margin = new Thickness(0, 0, 0, AppTheme.SpaceSM);
        HorizontalAlignment = HorizontalAlignment.Stretch;

        var dock = new DockPanel();

        _closeButton = new TextBlock
        {
            Text = IconLibrary.Close,
            FontFamily = AppTheme.FontIcon,
            FontSize = 10,
            Foreground = AppTheme.TextMuted,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(AppTheme.SpaceSM, 0, 0, 0),
            Cursor = Cursors.Hand
        };
        _closeButton.MouseEnter += (_, __) => _closeButton.Foreground = AppTheme.TextPrimary;
        _closeButton.MouseLeave += (_, __) => _closeButton.Foreground = AppTheme.TextMuted;
        _closeButton.MouseLeftButtonUp += (_, __) => DismissAnimated();
        DockPanel.SetDock(_closeButton, Dock.Right);
        dock.Children.Add(_closeButton);

        _iconBlock = new TextBlock
        {
            Text = icon,
            FontFamily = AppTheme.FontIcon,
            FontSize = 14,
            Foreground = border,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, AppTheme.SpaceSM, 0)
        };
        DockPanel.SetDock(_iconBlock, Dock.Left);
        dock.Children.Add(_iconBlock);

        _messageBlock = new TextBlock
        {
            Text = message,
            Foreground = AppTheme.TextPrimary,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            FontWeight = FontWeights.Medium,
            VerticalAlignment = VerticalAlignment.Center
        };
        dock.Children.Add(_messageBlock);

        Child = dock;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(AppTheme.ToastDuration) };
        _timer.Tick += (_, __) => DismissAnimated();
        _timer.Start();
    }

    private void ApplyThemeRefresh()
    {
        var (bg, border, _) = ResolveThemePalette(_type);
        Background = bg;
        BorderBrush = border;
        _iconBlock.Foreground = border;
        _messageBlock.Foreground = AppTheme.TextPrimary;
        _closeButton.Foreground = AppTheme.TextMuted;
    }

    private void DismissAnimated()
    {
        _timer.Stop();
        AnimationHelper.SlideOut(this, -20, AppTheme.AnimFast, () =>
        {
            if (_parent.Children.Contains(this))
            {
                _parent.Children.Remove(this);
            }
        });
    }

    private static (Brush Background, Brush Border, string Icon) ResolveThemePalette(ToastType type)
    {
        return type switch
        {
            ToastType.Success => (AppTheme.SuccessBg, AppTheme.Success, IconLibrary.CheckMark),
            ToastType.Warning => (AppTheme.WarningBg, AppTheme.Warning, IconLibrary.Warning),
            ToastType.Error => (AppTheme.ErrorBg, AppTheme.Danger, IconLibrary.ErrorBadge),
            _ => (AppTheme.InfoBg, AppTheme.Accent, IconLibrary.Info)
        };
    }
}

internal enum ToastType
{
    Success,
    Warning,
    Error,
    Info
}
