using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BIM765T.Revit.Agent.UI.Theme;

namespace BIM765T.Revit.Agent.UI.Components;

/// <summary>
/// Modern suggestion chip — tag-style pill with icon + hover glow.
/// Supports optional category color for context tags.
/// </summary>
internal sealed class SuggestionChip : Border
{
    internal SuggestionChip(string title, Action onClick, SolidColorBrush? accentColor = null)
    {
        var accent = accentColor ?? AppTheme.AccentBlue;
        var accentDim = accentColor != null ? accentColor : AppTheme.SubtleBorder;

        Margin = new Thickness(0, 0, AppTheme.SpaceSM, AppTheme.SpaceSM);
        CornerRadius = new CornerRadius(AppTheme.ButtonRadius);
        Background = AppTheme.SurfaceElevated;
        BorderBrush = accentDim;
        BorderThickness = new Thickness(1);
        Padding = new Thickness(AppTheme.SpaceMD, AppTheme.SpaceSM - 2, AppTheme.SpaceMD, AppTheme.SpaceSM - 2);
        Cursor = Cursors.Hand;

        // Render transform for hover scale
        RenderTransform = new ScaleTransform(1, 1);
        RenderTransformOrigin = new Point(0.5, 0.5);

        var content = new StackPanel { Orientation = Orientation.Horizontal };

        // Accent dot
        content.Children.Add(new Border
        {
            Width = 6,
            Height = 6,
            CornerRadius = new CornerRadius(3),
            Background = accent,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, AppTheme.SpaceSM, 0)
        });

        // Text
        content.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = AppTheme.TextSecondary,
            FontSize = AppTheme.FontSecondary,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });

        Child = content;

        // Hover effects
        MouseEnter += (_, _) =>
        {
            AnimationHelper.AnimateBackground(this, AppTheme.HoverBg.Color, AppTheme.AnimFast);
            AnimationHelper.AnimateBorderColor(this, accent.Color, AppTheme.AnimFast);
            AnimationHelper.ScaleTo(this, 1.03, AppTheme.AnimFast);
        };
        MouseLeave += (_, _) =>
        {
            AnimationHelper.AnimateBackground(this, AppTheme.SurfaceElevated.Color, AppTheme.AnimFast);
            AnimationHelper.AnimateBorderColor(this, accentDim.Color, AppTheme.AnimFast);
            AnimationHelper.ScaleTo(this, 1.0, AppTheme.AnimFast);
        };
        MouseLeftButtonUp += (_, _) => onClick();
    }

    /// <summary>Factory for context tag chips (colored border, like Wall_Module_TypeA).</summary>
    internal static SuggestionChip ContextTag(string tag, SolidColorBrush color)
    {
        var chip = new SuggestionChip(tag, () => { }, color);
        chip.Cursor = Cursors.Arrow; // non-clickable context tag
        return chip;
    }
}
