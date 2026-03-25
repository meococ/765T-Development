using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using BIM765T.Revit.Agent.UI.Theme;

namespace BIM765T.Revit.Agent.UI.Components;

/// <summary>
/// Spinning progress ring vẽ bằng DrawingContext — không cần image.
/// Dùng khi tool đang chạy để hiện visual feedback.
///
/// Dùng: var ring = new ProgressRing(24, accentBrush); panel.Children.Add(ring);
///       ring.Start(); / ring.Stop();
/// </summary>
internal sealed class ProgressRing : Border
{
    private static readonly Brush TrackColor = AppTheme.CardBorder;
    private readonly Brush _accentColor;
    private readonly double _size;
    private readonly DispatcherTimer _timer;
    private double _angle;
    private bool _spinning;

    internal ProgressRing(double size, Brush accentColor)
    {
        _size = size;
        _accentColor = accentColor;
        Width = size;
        Height = size;

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        _timer.Tick += (_, __) =>
        {
            _angle = (_angle + 15) % 360;
            InvalidateVisual();
        };

        Visibility = Visibility.Collapsed;
    }

    internal void Start()
    {
        _spinning = true;
        Visibility = Visibility.Visible;
        _timer.Start();
    }

    internal void Stop()
    {
        _spinning = false;
        _timer.Stop();
        Visibility = Visibility.Collapsed;
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (!_spinning) return;

        var center = new Point(_size / 2, _size / 2);
        var radius = _size / 2 - 2;

        // Track ring (mờ)
        dc.DrawEllipse(null, new Pen(TrackColor, 3), center, radius, radius);

        // Arc segment (accent) — vẽ cung tròn 90°
        var startRad = _angle * Math.PI / 180.0;
        var endRad = (_angle + 90) * Math.PI / 180.0;

        var startPoint = new Point(
            center.X + radius * Math.Cos(startRad),
            center.Y + radius * Math.Sin(startRad));
        var endPoint = new Point(
            center.X + radius * Math.Cos(endRad),
            center.Y + radius * Math.Sin(endRad));

        var figure = new PathFigure { StartPoint = startPoint, IsClosed = false, IsFilled = false };
        figure.Segments.Add(new ArcSegment(endPoint, new Size(radius, radius), 0, false, SweepDirection.Clockwise, true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);

        dc.DrawGeometry(null, new Pen(_accentColor, 3) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, geometry);
    }

    // BrushFrom removed — use AppTheme.Frozen() directly if needed
}
