using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace BIM765T.Revit.Agent.UI.Theme;

/// <summary>
/// Central icon library.
/// - Fallback path: Segoe glyphs for compatibility
/// - Premium path: vector icons for the primary cockpit surfaces so the pane matches the web review better
/// </summary>
internal static class IconLibrary
{
    // ── Navigation ──
    internal const string Home          = "\uE80F";
    internal const string WorkerChat    = "\uE8BD";
    internal const string QuickTools    = "\uE945";
    internal const string Inspector     = "\uE773";
    internal const string Workflows     = "\uE8FD";
    internal const string Evidence      = "\uE8B7";
    internal const string Activity      = "\uE9D2";
    internal const string Building      = "\uE805";

    // ── Actions ──
    internal const string Play          = "\uE768";
    internal const string Refresh       = "\uE72C";
    internal const string Settings      = "\uE713";
    internal const string Close         = "\uE711";
    internal const string More          = "\uE712";
    internal const string Save          = "\uE74E";
    internal const string Copy          = "\uE8C8";
    internal const string Delete        = "\uE74D";
    internal const string Edit          = "\uE70F";

    // ── Status ──
    internal const string CheckMark     = "\uE73E";
    internal const string ErrorBadge    = "\uEA39";
    internal const string Warning       = "\uE7BA";
    internal const string Info          = "\uE946";
    internal const string StatusCircle  = "\uEA3B";

    // ── Chevrons ──
    internal const string ChevronDown   = "\uE70D";
    internal const string ChevronRight  = "\uE76C";
    internal const string ChevronUp     = "\uE70E";
    internal const string ChevronLeft   = "\uE76B";

    // ── BIM / Domain ──
    internal const string Health        = "\uE8CB";
    internal const string Sheet         = "\uE8A5";
    internal const string View          = "\uE7B3";
    internal const string Element       = "\uE739";
    internal const string Parameter     = "\uE8F9";
    internal const string Context       = "\uE7C3";
    internal const string Camera        = "\uE722";
    internal const string Workset       = "\uE821";
    internal const string Graph         = "\uE9D9";
    internal const string Document      = "\uE8A5";
    internal const string Export        = "\uE898";
    internal const string Shield        = "\uE83D";
    internal const string Explain       = "\uE82F";
    internal const string Clock         = "\uE823";
    internal const string Flow          = "\uE8FD";

    // ── UI ──
    internal const string Pin           = "\uE718";
    internal const string Unpin         = "\uE77A";
    internal const string Expand        = "\uE740";
    internal const string Collapse      = "\uE73F";
    internal const string Filter        = "\uE71C";
    internal const string Search        = "\uE721";

    internal static FrameworkElement Create(string glyph, double size, Brush? foreground = null)
    {
        var accent = foreground ?? AppTheme.TextPrimary;
        if (TryCreateVector(glyph, size, accent, out var vectorIcon))
        {
            return vectorIcon;
        }

        return CreateGlyph(glyph, size, accent);
    }

    internal static Border CreateCircle(string glyph, double size, Brush iconColor, Brush? bgColor = null)
    {
        return new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(Math.Max(size / 5, 4)),
            Background = bgColor ?? Brushes.Transparent,
            Child = Create(glyph, size * 0.55, iconColor)
        };
    }

    private static TextBlock CreateGlyph(string glyph, double size, Brush foreground)
    {
        return new TextBlock
        {
            Text = glyph,
            FontFamily = AppTheme.FontIcon,
            FontSize = size,
            Foreground = foreground,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center
        };
    }

    private static bool TryCreateVector(string glyph, double size, Brush foreground, out FrameworkElement element)
    {
        var token = ResolveToken(glyph);
        if (string.IsNullOrWhiteSpace(token))
        {
            element = null!;
            return false;
        }

        var stroke = foreground as SolidColorBrush ?? AppTheme.AccentBlue;
        var viewbox = new Viewbox
        {
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            Child = BuildVectorIcon(token, stroke)
        };
        element = viewbox;
        return true;
    }

    private static string ResolveToken(string glyph)
    {
        if (glyph == WorkerChat) return "chat";
        if (glyph == QuickTools) return "command";
        if (glyph == Evidence) return "evidence";
        if (glyph == Activity) return "activity";
        if (glyph == Inspector || glyph == Search) return "search";
        if (glyph == Document || glyph == Sheet) return "document";
        if (glyph == View) return "view";
        if (glyph == Element) return "element";
        if (glyph == Flow || glyph == Workflows || glyph == Context) return "flow";
        if (glyph == Shield || glyph == Health) return "shield";
        if (glyph == Warning) return "warning";
        if (glyph == CheckMark) return "check";
        if (glyph == Info) return "info";
        if (glyph == Pin) return "pin";
        if (glyph == Clock) return "clock";
        if (glyph == Play) return "play";
        if (glyph == Copy) return "copy";
        if (glyph == Explain) return "spark";
        if (glyph == Building) return "building";
        return string.Empty;
    }

    private static Canvas BuildVectorIcon(string token, SolidColorBrush stroke)
    {
        var canvas = new Canvas
        {
            Width = 24,
            Height = 24
        };

        switch (token)
        {
            case "chat":
                canvas.Children.Add(Path("M5.5,6.5 C5.5,5.4 6.4,4.5 7.5,4.5 H16.5 C17.6,4.5 18.5,5.4 18.5,6.5 V13.5 C18.5,14.6 17.6,15.5 16.5,15.5 H11.0 L7.5,19 V15.5 H7.5 C6.4,15.5 5.5,14.6 5.5,13.5 Z", stroke, null, 1.8));
                canvas.Children.Add(Circle(9.0, 10.1, 0.9, stroke));
                canvas.Children.Add(Circle(12.0, 10.1, 0.9, stroke));
                canvas.Children.Add(Circle(15.0, 10.1, 0.9, stroke));
                break;

            case "command":
                canvas.Children.Add(Path("M5.0,6.0 L10.2,11.6 L5.0,17.0", stroke, null, 1.9));
                canvas.Children.Add(Path("M12.5,17.0 H19.0", stroke, null, 1.9));
                break;

            case "evidence":
                canvas.Children.Add(Path("M7.5,4.5 H14.5 L18.5,8.5 V18.0 C18.5,19.1 17.6,20.0 16.5,20.0 H7.5 C6.4,20.0 5.5,19.1 5.5,18.0 V6.5 C5.5,5.4 6.4,4.5 7.5,4.5 Z", stroke, null, 1.7));
                canvas.Children.Add(Path("M14.5,4.8 V8.6 H18.2", stroke, null, 1.7));
                canvas.Children.Add(Path("M8.5,14.0 L11.2,16.8 L16.0,11.5", stroke, null, 1.8));
                break;

            case "activity":
                canvas.Children.Add(Path("M4.5,16.0 H8.0 L10.0,11.0 L13.0,15.0 L15.4,8.8 L17.0,12.0 H19.5", stroke, null, 1.9));
                canvas.Children.Add(Path("M6.0,20.0 H18.0", stroke, null, 1.4));
                break;

            case "search":
                canvas.Children.Add(Path("M10.5,5.5 A5.0,5.0 0 1 1 10.49,5.5", stroke, null, 1.8));
                canvas.Children.Add(Path("M14.3,14.3 L18.5,18.5", stroke, null, 1.8));
                break;

            case "document":
                canvas.Children.Add(Path("M7.5,4.5 H14.5 L18.5,8.5 V18.0 C18.5,19.1 17.6,20.0 16.5,20.0 H7.5 C6.4,20.0 5.5,19.1 5.5,18.0 V6.5 C5.5,5.4 6.4,4.5 7.5,4.5 Z", stroke, null, 1.7));
                canvas.Children.Add(Path("M14.5,4.8 V8.6 H18.2", stroke, null, 1.7));
                canvas.Children.Add(Path("M8.5,12.0 H15.5", stroke, null, 1.6));
                canvas.Children.Add(Path("M8.5,15.0 H15.5", stroke, null, 1.6));
                break;

            case "view":
                canvas.Children.Add(Path("M3.8,12.0 C6.5,7.5 9.1,5.5 12.0,5.5 C14.9,5.5 17.5,7.5 20.2,12.0 C17.5,16.5 14.9,18.5 12.0,18.5 C9.1,18.5 6.5,16.5 3.8,12.0 Z", stroke, null, 1.7));
                canvas.Children.Add(Path("M12.0,9.0 A3.0,3.0 0 1 1 11.99,9.0", stroke, null, 1.7));
                break;

            case "element":
                canvas.Children.Add(Path("M12.0,4.8 L18.0,8.2 V15.8 L12.0,19.2 L6.0,15.8 V8.2 Z", stroke, null, 1.6));
                canvas.Children.Add(Path("M12.0,4.8 V12.0 L18.0,8.2", stroke, null, 1.4));
                canvas.Children.Add(Path("M12.0,12.0 L6.0,8.2", stroke, null, 1.4));
                canvas.Children.Add(Path("M12.0,12.0 V19.2", stroke, null, 1.4));
                break;

            case "flow":
                canvas.Children.Add(Path("M7.0,7.0 H11.0 V4.5 H16.5 V9.5 H11.0 V7.0", stroke, null, 1.7));
                canvas.Children.Add(Path("M13.8,9.5 V12.0", stroke, null, 1.7));
                canvas.Children.Add(Path("M6.0,12.0 H18.0", stroke, null, 1.5));
                canvas.Children.Add(Path("M8.0,12.0 V16.5 H5.5 V19.5 H10.5 V16.5 H8.0", stroke, null, 1.7));
                canvas.Children.Add(Path("M18.0,12.0 V16.5 H15.5 V19.5 H20.5 V16.5 H18.0", stroke, null, 1.7));
                break;

            case "shield":
                canvas.Children.Add(Path("M12.0,4.5 L18.5,7.4 V12.2 C18.5,16.1 15.9,18.6 12.0,20.1 C8.1,18.6 5.5,16.1 5.5,12.2 V7.4 Z", stroke, null, 1.7));
                canvas.Children.Add(Path("M9.0,12.2 L11.2,14.4 L15.2,10.2", stroke, null, 1.8));
                break;

            case "warning":
                canvas.Children.Add(Path("M12.0,5.0 L19.0,18.0 H5.0 Z", stroke, null, 1.7));
                canvas.Children.Add(Path("M12.0,9.3 V13.3", stroke, null, 1.8));
                canvas.Children.Add(Circle(12.0, 16.4, 0.85, stroke));
                break;

            case "check":
                canvas.Children.Add(Path("M5.5,12.7 L9.7,16.8 L18.5,8.3", stroke, null, 2.0));
                break;

            case "info":
                canvas.Children.Add(Path("M12.0,5.0 A7.0,7.0 0 1 1 11.99,5.0", stroke, null, 1.7));
                canvas.Children.Add(Circle(12.0, 8.5, 0.9, stroke));
                canvas.Children.Add(Path("M12.0,11.0 V16.0", stroke, null, 1.8));
                break;

            case "pin":
                canvas.Children.Add(Path("M9.0,5.5 H15.0 L14.0,10.2 L17.2,13.4 L16.1,14.5 L12.0,10.6 L7.9,14.5 L6.8,13.4 L10.0,10.2 Z", stroke, null, 1.6));
                canvas.Children.Add(Path("M12.0,10.6 V19.0", stroke, null, 1.6));
                break;

            case "clock":
                canvas.Children.Add(Path("M12.0,5.0 A7.0,7.0 0 1 1 11.99,5.0", stroke, null, 1.7));
                canvas.Children.Add(Path("M12.0,8.0 V12.4 L15.2,14.2", stroke, null, 1.8));
                break;

            case "play":
                canvas.Children.Add(Path("M8.0,6.0 L18.0,12.0 L8.0,18.0 Z", stroke, null, 1.8));
                break;

            case "copy":
                canvas.Children.Add(Path("M8.0,7.0 H16.0 C17.1,7.0 18.0,7.9 18.0,9.0 V17.0 C18.0,18.1 17.1,19.0 16.0,19.0 H8.0 C6.9,19.0 6.0,18.1 6.0,17.0 V9.0 C6.0,7.9 6.9,7.0 8.0,7.0 Z", stroke, null, 1.6));
                canvas.Children.Add(Path("M9.0,5.0 H17.0 C18.1,5.0 19.0,5.9 19.0,7.0", stroke, null, 1.5));
                break;

            case "spark":
                canvas.Children.Add(Path("M12.0,4.5 L13.8,9.0 L18.5,10.8 L13.8,12.6 L12.0,17.5 L10.2,12.6 L5.5,10.8 L10.2,9.0 Z", stroke, null, 1.7));
                break;

            case "building":
                canvas.Children.Add(Path("M6.0,20.0 H18.0", stroke, null, 1.6));
                canvas.Children.Add(Path("M7.0,20.0 V9.0 L12.0,6.0 L17.0,9.0 V20.0", stroke, null, 1.7));
                canvas.Children.Add(Path("M9.5,11.5 H10.5", stroke, null, 1.5));
                canvas.Children.Add(Path("M13.5,11.5 H14.5", stroke, null, 1.5));
                canvas.Children.Add(Path("M9.5,14.5 H10.5", stroke, null, 1.5));
                canvas.Children.Add(Path("M13.5,14.5 H14.5", stroke, null, 1.5));
                canvas.Children.Add(Path("M9.5,17.5 H10.5", stroke, null, 1.5));
                canvas.Children.Add(Path("M13.5,17.5 H14.5", stroke, null, 1.5));
                break;

            default:
                canvas.Children.Add(new TextBlock
                {
                    Text = token,
                    Foreground = stroke,
                    FontSize = 10
                });
                break;
        }

        return canvas;
    }

    private static Path Path(string data, Brush stroke, Brush? fill, double thickness)
    {
        return new Path
        {
            Data = Geometry.Parse(data),
            Stroke = stroke,
            Fill = fill ?? Brushes.Transparent,
            StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            SnapsToDevicePixels = true
        };
    }

    private static Ellipse Circle(double cx, double cy, double radius, Brush fill)
    {
        return new Ellipse
        {
            Width = radius * 2,
            Height = radius * 2,
            Fill = fill,
            StrokeThickness = 0,
            Margin = new Thickness(cx - radius, cy - radius, 0, 0)
        };
    }
}
