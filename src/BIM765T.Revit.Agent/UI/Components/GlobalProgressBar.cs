using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using BIM765T.Revit.Agent.UI.Theme;

namespace BIM765T.Revit.Agent.UI.Components;

/// <summary>
/// Global progress bar 3px — nằm dưới CompactHeader.
/// Indeterminate animation (gradient sweep).
/// Subscribes to InternalToolClient.ToolStarted/Completed.
/// </summary>
internal sealed class GlobalProgressBar : Border
{
    private readonly Border _indicator;
    private bool _isActive;

    internal GlobalProgressBar()
    {
        Height = 3;
        Background = AppTheme.CardBorder;
        ClipToBounds = true;
        Visibility = Visibility.Collapsed;

        _indicator = new Border
        {
            Width = 80,
            Height = 3,
            Background = AppTheme.Accent,
            CornerRadius = new CornerRadius(2),
            HorizontalAlignment = HorizontalAlignment.Left,
            RenderTransform = new TranslateTransform()
        };

        Child = _indicator;
    }

    internal void ApplyThemeRefresh()
    {
        Background = AppTheme.CardBorder;
        _indicator.Background = AppTheme.Accent;
    }

    /// <summary>Start indeterminate animation.</summary>
    internal void Start()
    {
        if (_isActive) return;
        _isActive = true;
        Visibility = Visibility.Visible;

        RunAnimation();
    }

    /// <summary>Stop and hide the progress bar.</summary>
    internal void Stop()
    {
        if (!_isActive) return;
        _isActive = false;

        AnimationHelper.FadeOut(this, AppTheme.AnimFast, () =>
        {
            Visibility = Visibility.Collapsed;
            var transform = _indicator.RenderTransform as TranslateTransform;
            if (transform != null)
            {
                transform.BeginAnimation(TranslateTransform.XProperty, null);
                transform.X = 0;
            }
        });
    }

    private void RunAnimation()
    {
        if (!_isActive) return;

        var transform = _indicator.RenderTransform as TranslateTransform ?? new TranslateTransform();
        _indicator.RenderTransform = transform;

        // Animate X from -80 (off-left) to ActualWidth (off-right)
        var totalWidth = ActualWidth > 0 ? ActualWidth : 300;
        var anim = new DoubleAnimation(-80, totalWidth, new Duration(TimeSpan.FromMilliseconds(1200)))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut },
            RepeatBehavior = RepeatBehavior.Forever
        };

        transform.BeginAnimation(TranslateTransform.XProperty, anim);
    }

    /// <inheritdoc/>
    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        if (_isActive) RunAnimation();
    }
}
