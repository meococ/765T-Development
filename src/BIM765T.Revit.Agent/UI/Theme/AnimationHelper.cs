using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace BIM765T.Revit.Agent.UI.Theme;

/// <summary>
/// WPF animation utilities — fade, slide, scale, color transition.
/// Thuần .NET 4.8 WPF, không cần NuGet.
/// Duration mặc định 250ms, easing QuadraticEase cho natural feel.
/// </summary>
internal static class AnimationHelper
{
    /// <summary>Fade element in (opacity 0 → 1).</summary>
    internal static void FadeIn(UIElement element, int durationMs = AppTheme.AnimNormal)
    {
        element.Opacity = 0;
        element.Visibility = Visibility.Visible;
        var anim = new DoubleAnimation(0, 1, Ms(durationMs))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        element.BeginAnimation(UIElement.OpacityProperty, anim);
    }

    /// <summary>Fade element out (opacity 1 → 0), optionally collapse after.</summary>
    internal static void FadeOut(UIElement element, int durationMs = AppTheme.AnimNormal, Action? onComplete = null)
    {
        var anim = new DoubleAnimation(element.Opacity, 0, Ms(durationMs))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };
        anim.Completed += (_, __) =>
        {
            element.Visibility = Visibility.Collapsed;
            onComplete?.Invoke();
        };
        element.BeginAnimation(UIElement.OpacityProperty, anim);
    }

    /// <summary>Slide element in from offset (translateY fromY → 0).</summary>
    internal static void SlideIn(UIElement element, double fromY = 20, int durationMs = AppTheme.AnimNormal)
    {
        var transform = element.RenderTransform as TranslateTransform ?? new TranslateTransform();
        element.RenderTransform = transform;
        element.Opacity = 0;
        element.Visibility = Visibility.Visible;

        var slideAnim = new DoubleAnimation(fromY, 0, Ms(durationMs))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        var fadeAnim = new DoubleAnimation(0, 1, Ms(durationMs))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };

        transform.BeginAnimation(TranslateTransform.YProperty, slideAnim);
        element.BeginAnimation(UIElement.OpacityProperty, fadeAnim);
    }

    /// <summary>Slide element out (translateY 0 → toY), collapse after.</summary>
    internal static void SlideOut(UIElement element, double toY = -20, int durationMs = AppTheme.AnimFast, Action? onComplete = null)
    {
        var transform = element.RenderTransform as TranslateTransform ?? new TranslateTransform();
        element.RenderTransform = transform;

        var slideAnim = new DoubleAnimation(0, toY, Ms(durationMs))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };
        var fadeAnim = new DoubleAnimation(element.Opacity, 0, Ms(durationMs))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };
        fadeAnim.Completed += (_, __) =>
        {
            element.Visibility = Visibility.Collapsed;
            // Reset transform for reuse
            transform.BeginAnimation(TranslateTransform.YProperty, null);
            transform.Y = 0;
            onComplete?.Invoke();
        };

        transform.BeginAnimation(TranslateTransform.YProperty, slideAnim);
        element.BeginAnimation(UIElement.OpacityProperty, fadeAnim);
    }

    /// <summary>Quick scale pulse for click feedback (1.0 → 1.05 → 1.0).</summary>
    internal static void ScalePulse(UIElement element, double peak = 1.05, int durationMs = 200)
    {
        var transform = element.RenderTransform as ScaleTransform;
        if (transform == null)
        {
            transform = new ScaleTransform(1, 1);
            element.RenderTransform = transform;
            element.RenderTransformOrigin = new Point(0.5, 0.5);
        }

        var half = Ms(durationMs / 2);
        var upX = new DoubleAnimation(1, peak, half)
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            AutoReverse = true
        };
        var upY = new DoubleAnimation(1, peak, half)
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            AutoReverse = true
        };

        transform.BeginAnimation(ScaleTransform.ScaleXProperty, upX);
        transform.BeginAnimation(ScaleTransform.ScaleYProperty, upY);
    }

    /// <summary>Scale element on hover (1.0 → scale). Call with 1.0 to reset.</summary>
    internal static void ScaleTo(UIElement element, double scale, int durationMs = AppTheme.AnimFast)
    {
        var transform = element.RenderTransform as ScaleTransform;
        if (transform == null)
        {
            transform = new ScaleTransform(1, 1);
            element.RenderTransform = transform;
            element.RenderTransformOrigin = new Point(0.5, 0.5);
        }

        var animX = new DoubleAnimation(scale, Ms(durationMs))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
        };
        var animY = new DoubleAnimation(scale, Ms(durationMs))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
        };

        transform.BeginAnimation(ScaleTransform.ScaleXProperty, animX);
        transform.BeginAnimation(ScaleTransform.ScaleYProperty, animY);
    }

    /// <summary>Animate background color of a Border.</summary>
    internal static void AnimateBackground(Border border, Color toColor, int durationMs = AppTheme.AnimFast)
    {
        var anim = new ColorAnimation(toColor, Ms(durationMs))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
        };

        // Need a non-frozen brush for animation
        if (border.Background is SolidColorBrush existing && existing.IsFrozen)
        {
            border.Background = new SolidColorBrush(existing.Color);
        }
        else if (border.Background == null)
        {
            border.Background = new SolidColorBrush(Colors.Transparent);
        }

        border.Background.BeginAnimation(SolidColorBrush.ColorProperty, anim);
    }

    /// <summary>Animate border color of a Border.</summary>
    internal static void AnimateBorderColor(Border border, Color toColor, int durationMs = AppTheme.AnimFast)
    {
        var anim = new ColorAnimation(toColor, Ms(durationMs))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
        };

        if (border.BorderBrush is SolidColorBrush existing && existing.IsFrozen)
        {
            border.BorderBrush = new SolidColorBrush(existing.Color);
        }
        else if (border.BorderBrush == null)
        {
            border.BorderBrush = new SolidColorBrush(Colors.Transparent);
        }

        border.BorderBrush.BeginAnimation(SolidColorBrush.ColorProperty, anim);
    }

    /// <summary>Animate MaxHeight for expand/collapse effect.</summary>
    internal static void AnimateHeight(FrameworkElement element, double fromHeight, double toHeight, int durationMs = AppTheme.AnimNormal, Action? onComplete = null)
    {
        element.ClipToBounds = true;
        var anim = new DoubleAnimation(fromHeight, toHeight, Ms(durationMs))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
        };
        if (onComplete != null)
        {
            anim.Completed += (_, __) => onComplete();
        }
        element.BeginAnimation(FrameworkElement.MaxHeightProperty, anim);
    }

    private static Duration Ms(int milliseconds) => new Duration(TimeSpan.FromMilliseconds(milliseconds));
}
