using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BIM765T.Revit.Agent.UI;

/// <summary>
/// Factory tao ribbon icons bang WPF DrawingContext - khong can file anh.
/// 6 bo icons: Chat, Context, HealthCheck, Warnings, Snapshot, Settings.
/// Moi bo co 16x16 (small) va 32x32 (large).
///
/// Design language v2:
/// - Rounded-rect background with radial gradient for depth
/// - Subtle top-edge highlight (inner glow simulation)
/// - Round LineCap/LineJoin on all strokes
/// - Cohesive color palette aligned with AppTheme
/// - Clean shapes optimized for 16×16 readability
/// </summary>
internal static class RibbonIconFactory
{
    // Chat / Assistant
    internal static ImageSource CreateAgentSmall()  => Render(16, DrawAgent);
    internal static ImageSource CreateAgentLarge()  => Render(32, DrawAgent);

    // ── Context ──
    internal static ImageSource CreateContextSmall() => Render(16, DrawContext);
    internal static ImageSource CreateContextLarge() => Render(32, DrawContext);

    // ── Health Check ──
    internal static ImageSource CreateHealthSmall()  => Render(16, DrawHealth);
    internal static ImageSource CreateHealthLarge()  => Render(32, DrawHealth);

    // ── Warnings ──
    internal static ImageSource CreateWarningsSmall() => Render(16, DrawWarnings);
    internal static ImageSource CreateWarningsLarge() => Render(32, DrawWarnings);

    // ── Snapshot ──
    internal static ImageSource CreateSnapshotSmall() => Render(16, DrawSnapshot);
    internal static ImageSource CreateSnapshotLarge() => Render(32, DrawSnapshot);

    // ── Settings ──
    internal static ImageSource CreateSettingsSmall() => Render(16, DrawSettings);
    internal static ImageSource CreateSettingsLarge() => Render(32, DrawSettings);

    private static ImageSource Render(int size, Action<DrawingContext, double> draw)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            draw(dc, size / 32.0);
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    // ══════════════════════════════════════════════════════════════
    // Shared: draw background card with gradient + subtle top highlight
    // ══════════════════════════════════════════════════════════════
    private static void DrawBg(DrawingContext dc, double s, string gradFrom, string gradTo)
    {
        var r = 7 * s;
        var bg = Gradient(gradFrom, gradTo);
        dc.DrawRoundedRectangle(bg, null, RAt(1, 1, 30, 30, s), r, r);

        // Subtle inner border (1px lighter at top for depth illusion)
        var highlight = Frozen("#FFFFFF", 0.10);
        dc.DrawRoundedRectangle(null, RoundPen(highlight, 1.0 * s), RAt(1.5, 1.5, 29, 29, s), r, r);
    }

    // ══════════════════════════════════════════════════════════════
    // Chat/Assistant - modern spark/diamond AI icon
    // ══════════════════════════════════════════════════════════════
    private static void DrawAgent(DrawingContext dc, double s)
    {
        DrawBg(dc, s, "#0C1631", "#1E40AF");
        var white = Frozen("#F0F9FF");
        var cyan  = Frozen("#22D3EE");
        var cyanDim = Frozen("#22D3EE", 0.35);

        // Outer glow ring (subtle)
        dc.DrawEllipse(cyanDim, null, P(16, 16, s), 10 * s, 10 * s);

        // Central diamond/spark shape (AI indicator)
        var spark = new StreamGeometry();
        using (var ctx = spark.Open())
        {
            ctx.BeginFigure(P(16, 5.5, s), true, true);
            ctx.LineTo(P(19.5, 13, s), true, false);
            ctx.LineTo(P(26.5, 16, s), true, false);
            ctx.LineTo(P(19.5, 19, s), true, false);
            ctx.LineTo(P(16, 26.5, s), true, false);
            ctx.LineTo(P(12.5, 19, s), true, false);
            ctx.LineTo(P(5.5, 16, s), true, false);
            ctx.LineTo(P(12.5, 13, s), true, false);
        }
        spark.Freeze();
        dc.DrawGeometry(cyan, RoundPen(white, 1.2 * s), spark);

        // Inner dot
        dc.DrawEllipse(white, null, P(16, 16, s), 2.2 * s, 2.2 * s);
    }

    // ══════════════════════════════════════════════════════════════
    // Context — document page + crosshair pin
    // ══════════════════════════════════════════════════════════════
    private static void DrawContext(DrawingContext dc, double s)
    {
        DrawBg(dc, s, "#0C2E2E", "#0D9488");
        var white = Frozen("#ECFDF5");
        var mint  = Frozen("#6EE7B7");

        // Document body (rounded rect)
        dc.DrawRoundedRectangle(null, RoundPen(white, 1.5 * s), RAt(7, 5, 14, 19, s), 2.5 * s, 2.5 * s);
        // Document fold corner
        var fold = new StreamGeometry();
        using (var ctx = fold.Open())
        {
            ctx.BeginFigure(P(15, 5, s), true, true);
            ctx.LineTo(P(21, 5, s), true, false);
            ctx.LineTo(P(21, 10, s), true, false);
            ctx.LineTo(P(15, 10, s), true, false);
        }
        fold.Freeze();
        dc.DrawGeometry(Frozen("#0D9488"), RoundPen(white, 1.0 * s), fold);

        // Text lines
        dc.DrawLine(RoundPen(white, 1.4 * s), P(10, 14, s), P(18, 14, s));
        dc.DrawLine(RoundPen(white, 1.4 * s), P(10, 17.5, s), P(16, 17.5, s));
        dc.DrawLine(RoundPen(white, 1.4 * s), P(10, 21, s), P(14, 21, s));

        // Crosshair pin (top-right)
        dc.DrawEllipse(null, RoundPen(mint, 1.6 * s), P(23.5, 10, s), 3.5 * s, 3.5 * s);
        dc.DrawLine(RoundPen(mint, 1.2 * s), P(23.5, 6, s), P(23.5, 14, s));
        dc.DrawLine(RoundPen(mint, 1.2 * s), P(19.5, 10, s), P(27.5, 10, s));
        dc.DrawEllipse(mint, null, P(23.5, 10, s), 1.2 * s, 1.2 * s);
    }

    // ══════════════════════════════════════════════════════════════
    // Health Check — filled shield with bold checkmark
    // ══════════════════════════════════════════════════════════════
    private static void DrawHealth(DrawingContext dc, double s)
    {
        DrawBg(dc, s, "#052E1C", "#059669");
        var white = Frozen("#F0FDF4");
        var shieldFill = Frozen("#FFFFFF", 0.15);

        // Shield shape (filled for presence)
        var shield = new StreamGeometry();
        using (var ctx = shield.Open())
        {
            ctx.BeginFigure(P(16, 5, s), true, true);
            ctx.LineTo(P(26, 9.5, s), true, false);
            ctx.BezierTo(P(25.5, 18, s), P(21, 23, s), P(16, 27, s), true, false);
            ctx.BezierTo(P(11, 23, s), P(6.5, 18, s), P(6, 9.5, s), true, false);
        }
        shield.Freeze();
        dc.DrawGeometry(shieldFill, RoundPen(white, 1.8 * s), shield);

        // Bold checkmark
        var check = new StreamGeometry();
        using (var ctx = check.Open())
        {
            ctx.BeginFigure(P(10.5, 15.5, s), false, false);
            ctx.LineTo(P(14, 19.5, s), true, false);
            ctx.LineTo(P(21.5, 11.5, s), true, false);
        }
        check.Freeze();
        dc.DrawGeometry(null, RoundPen(white, 2.4 * s), check);
    }

    // ══════════════════════════════════════════════════════════════
    // Warnings — refined triangle with rounded joins
    // ══════════════════════════════════════════════════════════════
    private static void DrawWarnings(DrawingContext dc, double s)
    {
        DrawBg(dc, s, "#451A03", "#D97706");
        var white = Frozen("#FFFBEB");
        var triFill = Frozen("#FFFFFF", 0.12);

        // Triangle with rounded corners (via bezier approximation)
        var tri = new StreamGeometry();
        using (var ctx = tri.Open())
        {
            ctx.BeginFigure(P(16, 6, s), true, true);
            ctx.LineTo(P(27, 25, s), true, false);
            ctx.LineTo(P(5, 25, s), true, false);
        }
        tri.Freeze();
        dc.DrawGeometry(triFill, RoundPen(white, 1.8 * s), tri);

        // Exclamation mark (round cap line + dot)
        dc.DrawLine(RoundPen(white, 2.2 * s), P(16, 13, s), P(16, 19, s));
        dc.DrawEllipse(white, null, P(16, 22.2, s), 1.4 * s, 1.4 * s);
    }

    // ══════════════════════════════════════════════════════════════
    // Snapshot — clean camera with viewfinder + flash
    // ══════════════════════════════════════════════════════════════
    private static void DrawSnapshot(DrawingContext dc, double s)
    {
        DrawBg(dc, s, "#1E1145", "#7C3AED");
        var white = Frozen("#F5F3FF");
        var violet = Frozen("#C4B5FD");

        // Camera top hump
        var hump = new StreamGeometry();
        using (var ctx = hump.Open())
        {
            ctx.BeginFigure(P(11, 10, s), true, true);
            ctx.LineTo(P(13, 7, s), true, false);
            ctx.LineTo(P(19, 7, s), true, false);
            ctx.LineTo(P(21, 10, s), true, false);
        }
        hump.Freeze();
        dc.DrawGeometry(null, RoundPen(white, 1.5 * s), hump);

        // Camera body
        dc.DrawRoundedRectangle(null, RoundPen(white, 1.6 * s), RAt(5.5, 10, 21, 15, s), 3 * s, 3 * s);

        // Lens (outer ring + inner filled)
        dc.DrawEllipse(null, RoundPen(white, 1.5 * s), P(16, 17.5, s), 4.5 * s, 4.5 * s);
        dc.DrawEllipse(null, RoundPen(violet, 1.0 * s), P(16, 17.5, s), 2.5 * s, 2.5 * s);
        dc.DrawEllipse(white, null, P(16, 17.5, s), 1.2 * s, 1.2 * s);

        // Flash indicator (top-right)
        dc.DrawEllipse(violet, null, P(23.5, 13, s), 1.3 * s, 1.3 * s);
    }

    // ══════════════════════════════════════════════════════════════
    // Settings — smooth gear with proper teeth using arcs
    // ══════════════════════════════════════════════════════════════
    private static void DrawSettings(DrawingContext dc, double s)
    {
        DrawBg(dc, s, "#0F172A", "#475569");
        var white = Frozen("#F1F5F9");
        var slate = Frozen("#CBD5E1");

        // Gear: generate teeth as a polygon ring
        var gear = BuildGearGeometry(s, 16, 16, outerR: 10, innerR: 7.5, teeth: 8, toothDepth: 2.8);
        dc.DrawGeometry(Frozen("#FFFFFF", 0.12), RoundPen(white, 1.5 * s), gear);

        // Inner circle (hub)
        dc.DrawEllipse(Frozen("#334155"), RoundPen(white, 1.5 * s), P(16, 16, s), 3.5 * s, 3.5 * s);

        // Hub dot
        dc.DrawEllipse(slate, null, P(16, 16, s), 1.5 * s, 1.5 * s);
    }

    /// <summary>Builds a gear-shaped StreamGeometry with smooth teeth.</summary>
    private static StreamGeometry BuildGearGeometry(double s, double cx, double cy, double outerR, double innerR, int teeth, double toothDepth)
    {
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            var totalPoints = teeth * 4;
            var angleStep = 360.0 / totalPoints;
            var toothHalfWidth = angleStep * 0.8;

            var first = true;
            for (int i = 0; i < teeth; i++)
            {
                var baseAngle = i * (360.0 / teeth);

                // Outer tooth peak (two points)
                var a1 = (baseAngle - toothHalfWidth) * Math.PI / 180.0;
                var a2 = (baseAngle + toothHalfWidth) * Math.PI / 180.0;
                var r1 = outerR + toothDepth;

                // Valley between teeth (two points)
                var a3 = (baseAngle + 180.0 / teeth - toothHalfWidth * 0.5) * Math.PI / 180.0;
                var a4 = (baseAngle + 180.0 / teeth + toothHalfWidth * 0.5) * Math.PI / 180.0;

                var p1 = P(cx + r1 * Math.Cos(a1), cy + r1 * Math.Sin(a1), s);
                var p2 = P(cx + r1 * Math.Cos(a2), cy + r1 * Math.Sin(a2), s);
                var p3 = P(cx + innerR * Math.Cos(a3), cy + innerR * Math.Sin(a3), s);
                var p4 = P(cx + innerR * Math.Cos(a4), cy + innerR * Math.Sin(a4), s);

                if (first)
                {
                    ctx.BeginFigure(p1, true, true);
                    first = false;
                }
                else
                {
                    ctx.LineTo(p1, true, false);
                }

                ctx.LineTo(p2, true, false);
                ctx.LineTo(p3, true, false);
                ctx.LineTo(p4, true, false);
            }
        }
        geo.Freeze();
        return geo;
    }

    // ── Helpers ──

    private static Rect RAt(double x, double y, double w, double h, double s)
        => new Rect(x * s, y * s, w * s, h * s);

    private static Point P(double x, double y, double s)
        => new Point(x * s, y * s);

    /// <summary>Creates a frozen Pen with Round line cap/join for modern look.</summary>
    private static Pen RoundPen(Brush brush, double thickness)
    {
        var pen = new Pen(brush, thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
        pen.Freeze();
        return pen;
    }

    private static SolidColorBrush Frozen(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    /// <summary>Creates a frozen SolidColorBrush with opacity.</summary>
    private static SolidColorBrush Frozen(string hex, double opacity)
    {
        var color = (Color)ColorConverter.ConvertFromString(hex);
        color.A = (byte)(255 * Math.Max(0, Math.Min(1, opacity)));
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static LinearGradientBrush Gradient(string from, string to)
    {
        var brush = new LinearGradientBrush(
            (Color)ColorConverter.ConvertFromString(from),
            (Color)ColorConverter.ConvertFromString(to),
            new Point(0, 0), new Point(0.7, 1));
        brush.Freeze();
        return brush;
    }
}
