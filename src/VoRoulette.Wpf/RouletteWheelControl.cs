using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using VoRoulette.Core;

namespace VoRoulette.Wpf;

public sealed class RouletteWheelControl : FrameworkElement
{
    private readonly List<WheelSegment> _segments = [];

    public double RotationRadians
    {
        get => (double)GetValue(RotationRadiansProperty);
        set => SetValue(RotationRadiansProperty, value);
    }

    public static readonly DependencyProperty RotationRadiansProperty =
        DependencyProperty.Register(nameof(RotationRadians), typeof(double), typeof(RouletteWheelControl),
            new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public void RebuildSegments(IEnumerable<RouletteEntry> entries)
    {
        _segments.Clear();
        var normalized = RouletteEngine.Normalize(entries).Where(e => e.Enabled).ToArray();
        if (normalized.Length == 0)
        {
            InvalidateVisual();
            return;
        }

        var total = normalized.Sum(x => x.Weight);
        var angle = 0d;
        foreach (var item in normalized)
        {
            var span = Math.PI * 2 * (item.Weight / total);
            _segments.Add(new WheelSegment(item.Name, angle, angle + span, BuildBrush(angle + span / 2)));
            angle += span;
        }

        InvalidateVisual();
    }

    public string PickCurrentName()
    {
        if (_segments.Count == 0)
        {
            return string.Empty;
        }

        var pointerAngle = NormalizeAngle(-RotationRadians - Math.PI / 2);
        foreach (var segment in _segments)
        {
            if (pointerAngle >= segment.Start && pointerAngle < segment.End)
            {
                return segment.Name;
            }
        }

        return _segments[^1].Name;
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        var width = ActualWidth;
        var height = ActualHeight;
        var size = Math.Min(width, height);
        if (size <= 0)
        {
            return;
        }

        var center = new Point(width / 2, height / 2);
        var radius = size * 0.45;

        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, width, height));

        dc.PushTransform(new RotateTransform(RotationRadians * 180 / Math.PI, center.X, center.Y));
        foreach (var segment in _segments)
        {
            DrawSegment(dc, center, radius, segment);
        }

        dc.Pop();

        dc.DrawEllipse(Brushes.White, new Pen(Brushes.Black, 2), center, radius * 0.1, radius * 0.1);

        var pointer = new StreamGeometry();
        using (var ctx = pointer.Open())
        {
            ctx.BeginFigure(new Point(center.X, center.Y - radius - 22), true, true);
            ctx.LineTo(new Point(center.X - 12, center.Y - radius + 2), true, false);
            ctx.LineTo(new Point(center.X + 12, center.Y - radius + 2), true, false);
        }

        pointer.Freeze();
        dc.DrawGeometry(Brushes.IndianRed, new Pen(Brushes.DarkRed, 1), pointer);
    }

    private static void DrawSegment(DrawingContext dc, Point center, double radius, WheelSegment segment)
    {
        var startPoint = new Point(center.X + Math.Cos(segment.Start) * radius, center.Y + Math.Sin(segment.Start) * radius);
        var endPoint = new Point(center.X + Math.Cos(segment.End) * radius, center.Y + Math.Sin(segment.End) * radius);
        var isLargeArc = segment.End - segment.Start > Math.PI;

        var geom = new StreamGeometry();
        using (var ctx = geom.Open())
        {
            ctx.BeginFigure(center, true, true);
            ctx.LineTo(startPoint, true, false);
            ctx.ArcTo(endPoint, new Size(radius, radius), 0, isLargeArc, SweepDirection.Clockwise, true, false);
        }

        geom.Freeze();
        dc.DrawGeometry(segment.Brush, new Pen(Brushes.White, 1), geom);

        var mid = (segment.Start + segment.End) / 2;
        var span = segment.End - segment.Start;
        // Sector centroid distance from center: r = 4R*sin(theta/2)/(3*theta)
        var labelRadius = span > 0.0001
            ? (4 * radius * Math.Sin(span / 2)) / (3 * span)
            : radius * 0.6;
        var availableWidth = Math.Max(28, labelRadius * span * 0.85);
        var fontSize = Math.Max(12, Math.Min(radius * 0.08, availableWidth * 0.6));
        var labelPoint = new Point(
            center.X + Math.Cos(mid) * labelRadius,
            center.Y + Math.Sin(mid) * labelRadius);

        var text = new FormattedText(
            segment.Name,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            fontSize,
            Brushes.Black,
            VisualTreeHelper.GetDpi(Application.Current.MainWindow).PixelsPerDip);

        var scaleX = text.Width > availableWidth ? availableWidth / text.Width : 1.0;
        dc.PushTransform(new ScaleTransform(scaleX, 1.0, labelPoint.X, labelPoint.Y));
        dc.DrawText(text, new Point(labelPoint.X - text.Width / 2, labelPoint.Y - text.Height / 2));
        dc.Pop();
    }

    private static SolidColorBrush BuildBrush(double radians)
    {
        var deg = radians * 180 / Math.PI;
        var hue = ((deg + 150) % 360 + 360) % 360;
        var color = ColorFromHsl(hue, 0.7, 0.75);
        return new SolidColorBrush(color);
    }

    private static Color ColorFromHsl(double h, double s, double l)
    {
        var c = (1 - Math.Abs(2 * l - 1)) * s;
        var x = c * (1 - Math.Abs((h / 60) % 2 - 1));
        var m = l - c / 2;

        (double r, double g, double b) rgb = h switch
        {
            < 60 => (c, x, 0),
            < 120 => (x, c, 0),
            < 180 => (0, c, x),
            < 240 => (0, x, c),
            < 300 => (x, 0, c),
            _ => (c, 0, x)
        };

        return Color.FromRgb(
            (byte)Math.Round((rgb.r + m) * 255),
            (byte)Math.Round((rgb.g + m) * 255),
            (byte)Math.Round((rgb.b + m) * 255));
    }

    private static double NormalizeAngle(double angle)
    {
        var tau = Math.PI * 2;
        var normalized = angle % tau;
        return normalized < 0 ? normalized + tau : normalized;
    }

    private sealed record WheelSegment(string Name, double Start, double End, SolidColorBrush Brush);
}
