using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BIM765T.Revit.Agent.UI.Theme;

namespace BIM765T.Revit.Agent.UI.Components;

/// <summary>
/// Status badge — pill shape with modern styling.
/// v3: oval badges with consistent padding, border glow, mono font.
/// </summary>
internal sealed class StatusBadge : Border
{
    private readonly TextBlock _text;

    internal StatusBadge(string label, Brush foreground)
    {
        CornerRadius = new CornerRadius(AppTheme.BadgeRadius);
        Padding = new Thickness(8, 3, 8, 3);
        Margin = new Thickness(0, 0, 6, 0);
        MinHeight = 20;
        BorderThickness = new Thickness(1);

        if (foreground is SolidColorBrush solid)
        {
            var colorStr = solid.Color.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Background = AppTheme.FrozenAlpha(colorStr, 0.15);
            BorderBrush = AppTheme.FrozenAlpha(colorStr, 0.40);
        }
        else
        {
            Background = AppTheme.SurfaceElevated;
            BorderBrush = AppTheme.SubtleBorder;
        }

        _text = new TextBlock
        {
            Text = label,
            Foreground = foreground,
            FontWeight = FontWeights.SemiBold,
            FontSize = AppTheme.FontCaption,
            FontFamily = AppTheme.FontMono,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        Child = _text;
    }

    internal void Update(string label, Brush foreground)
    {
        _text.Text = label;
        _text.Foreground = foreground;

        if (foreground is SolidColorBrush solid)
        {
            var colorStr = solid.Color.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Background = AppTheme.FrozenAlpha(colorStr, 0.15);
            BorderBrush = AppTheme.FrozenAlpha(colorStr, 0.40);
        }
    }
}
