using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using BIM765T.Revit.Agent.UI.Theme;

namespace BIM765T.Revit.Agent.UI.Components;

/// <summary>
/// Nút bấm v2 — icon MDL2/chữ đầu + text + subtitle + loading state + animated hover.
/// Full variant: 56px min height. Compact variant: 40px, no subtitle.
///
/// Dùng: new ActionButton("Chạy", "Mô tả", AppTheme.Accent, onClick);
///       new ActionButton("Chạy", "Mô tả", AppTheme.Accent, onClick, iconChar: "\uE768");
///       ActionButton.Compact("Health", AppTheme.Success, onClick, "\uE73E");
/// </summary>
internal sealed class ActionButton : Border
{
    private readonly Action _onClick;
    private readonly TextBlock _titleBlock;
    private readonly TextBlock? _subtitleBlock;
    private readonly Border _iconBorder;
    private readonly ProgressRing _spinner;
    private readonly string _originalSubtitle;
    private bool _enabled = true;
    private bool _loading;

    /// <summary>Full-size ActionButton with icon + title + subtitle.</summary>
    internal ActionButton(string title, string subtitle, Brush accentColor, Action onClick, string? iconChar = null)
    {
        _onClick = onClick;
        _originalSubtitle = subtitle;

        // ── Self styling ──
        Background = new SolidColorBrush(AppTheme.CardBackground.Color);
        BorderBrush = new SolidColorBrush(AppTheme.CardBorder.Color);
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(AppTheme.ButtonRadius);
        Padding = new Thickness(AppTheme.SpaceMD, AppTheme.SpaceSM + 2, AppTheme.SpaceMD, AppTheme.SpaceSM + 2);
        MinHeight = AppTheme.ButtonMinHeight;
        Margin = new Thickness(0, 0, 0, AppTheme.SpaceSM);
        Cursor = Cursors.Hand;

        // ── Render transform for scale animation ──
        RenderTransform = new ScaleTransform(1, 1);
        RenderTransformOrigin = new Point(0.5, 0.5);

        // ── Icon (MDL2 char or first letter) ──
        var iconContent = BuildIconContent(title, accentColor, iconChar);
        _iconBorder = new Border
        {
            Width = 36,
            Height = 36,
            CornerRadius = new CornerRadius(AppTheme.ButtonRadius),
            Background = accentColor,
            Margin = new Thickness(0, 0, AppTheme.SpaceMD, 0),
            Child = iconContent
        };

        // ── Spinner (hidden by default) ──
        _spinner = new ProgressRing(20, accentColor);

        // ── Text ──
        _titleBlock = new TextBlock
        {
            Text = title,
            Foreground = AppTheme.TextPrimary,
            FontSize = AppTheme.FontSection,
            FontWeight = FontWeights.SemiBold
        };

        _subtitleBlock = new TextBlock
        {
            Text = subtitle,
            Foreground = AppTheme.TextMuted,
            FontSize = AppTheme.FontSecondary,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 0)
        };

        var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        textStack.Children.Add(_titleBlock);
        textStack.Children.Add(_subtitleBlock);

        // ── Layout: [icon/spinner] [text] ──
        var dock = new DockPanel();
        DockPanel.SetDock(_iconBorder, Dock.Left);
        dock.Children.Add(_iconBorder);
        dock.Children.Add(textStack);

        Child = dock;
        SetupInteraction();
    }

    /// <summary>Private constructor for compact variant.</summary>
    private ActionButton(string title, Brush accentColor, Action onClick, string? iconChar, bool compact)
    {
        _onClick = onClick;
        _originalSubtitle = string.Empty;

        Background = new SolidColorBrush(AppTheme.CardBackground.Color);
        BorderBrush = new SolidColorBrush(AppTheme.CardBorder.Color);
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(AppTheme.ButtonRadius);
        Padding = new Thickness(AppTheme.SpaceSM + 2, AppTheme.SpaceSM, AppTheme.SpaceSM + 2, AppTheme.SpaceSM);
        MinHeight = AppTheme.ButtonCompactHeight;
        Margin = new Thickness(0, 0, 0, AppTheme.SpaceXS);
        Cursor = Cursors.Hand;

        RenderTransform = new ScaleTransform(1, 1);
        RenderTransformOrigin = new Point(0.5, 0.5);

        var iconContent = BuildIconContent(title, accentColor, iconChar);
        _iconBorder = new Border
        {
            Width = 26,
            Height = 26,
            CornerRadius = new CornerRadius(6),
            Background = accentColor,
            Margin = new Thickness(0, 0, AppTheme.SpaceSM, 0),
            Child = iconContent
        };

        _spinner = new ProgressRing(16, accentColor);

        _titleBlock = new TextBlock
        {
            Text = title,
            Foreground = AppTheme.TextPrimary,
            FontSize = AppTheme.FontBody,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };

        var dock = new DockPanel();
        DockPanel.SetDock(_iconBorder, Dock.Left);
        dock.Children.Add(_iconBorder);
        dock.Children.Add(_titleBlock);

        Child = dock;
        SetupInteraction();
    }

    /// <summary>Factory for compact button (40px, icon + title only, no subtitle).</summary>
    internal static ActionButton Compact(string title, Brush accentColor, Action onClick, string? iconChar = null)
    {
        return new ActionButton(title, accentColor, onClick, iconChar, compact: true);
    }

    // ── Public API ──

    /// <summary>Set subtitle text (full variant only).</summary>
    internal void SetSubtitle(string text)
    {
        if (_subtitleBlock != null) _subtitleBlock.Text = text;
    }

    /// <summary>Toggle loading state — swap icon ↔ spinner, update subtitle.</summary>
    internal void SetLoading(bool loading)
    {
        if (_loading == loading) return;
        _loading = loading;

        if (loading)
        {
            _iconBorder.Child = _spinner;
            _spinner.Start();
            if (_subtitleBlock != null) _subtitleBlock.Text = "Processing...";
            Cursor = Cursors.Wait;
        }
        else
        {
            _spinner.Stop();
            _iconBorder.Child = BuildIconContent(_titleBlock.Text, (Brush)_iconBorder.Background, null);
            if (_subtitleBlock != null) _subtitleBlock.Text = _originalSubtitle;
            Cursor = Cursors.Hand;
        }
    }

    /// <summary>Enable/disable button.</summary>
    internal void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        Opacity = enabled ? 1.0 : 0.5;
        Cursor = enabled ? Cursors.Hand : Cursors.Arrow;
        Background = new SolidColorBrush(enabled ? AppTheme.CardBackground.Color : AppTheme.PageBackground.Color);
        _titleBlock.Foreground = enabled ? AppTheme.TextPrimary : AppTheme.TextDisabled;
        if (_subtitleBlock != null)
            _subtitleBlock.Foreground = enabled ? AppTheme.TextMuted : AppTheme.TextDisabled;
    }

    // ── Private helpers ──

    private void SetupInteraction()
    {
        MouseEnter += (_, __) =>
        {
            if (!_enabled || _loading) return;
            AnimationHelper.AnimateBackground(this, AppTheme.HoverBg.Color, AppTheme.AnimFast);
            AnimationHelper.AnimateBorderColor(this, AppTheme.HoverBorder.Color, AppTheme.AnimFast);
            AnimationHelper.ScaleTo(this, 1.02, AppTheme.AnimFast);
        };
        MouseLeave += (_, __) =>
        {
            if (!_enabled || _loading) return;
            AnimationHelper.AnimateBackground(this, AppTheme.CardBackground.Color, AppTheme.AnimFast);
            AnimationHelper.AnimateBorderColor(this, AppTheme.CardBorder.Color, AppTheme.AnimFast);
            AnimationHelper.ScaleTo(this, 1.0, AppTheme.AnimFast);
        };
        MouseLeftButtonDown += (_, __) =>
        {
            if (!_enabled || _loading) return;
            AnimationHelper.ScaleTo(this, 0.98, 80);
        };
        MouseLeftButtonUp += (_, __) =>
        {
            if (!_enabled || _loading) return;
            AnimationHelper.ScaleTo(this, 1.02, 80);
            _onClick();
        };
    }

    private static UIElement BuildIconContent(string title, Brush accentColor, string? iconChar)
    {
        if (!string.IsNullOrEmpty(iconChar))
        {
            return new TextBlock
            {
                Text = iconChar,
                FontFamily = AppTheme.FontIcon,
                FontSize = 16,
                Foreground = AppTheme.PageBackground,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        return new TextBlock
        {
            Text = title.Length > 0 ? title.Substring(0, 1).ToUpperInvariant() : "?",
            Foreground = AppTheme.PageBackground,
            FontWeight = FontWeights.Bold,
            FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
    }
}
