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
    private DateTime _lastPointerRenderAt = DateTime.MinValue;
    private double _lastRotationRadians;
    private double _smoothedAngularSpeed;
    private double _pointerFlexX;
    private double _pointerFlexVelocityX;

    public double RotationRadians
    {
        get => (double)GetValue(RotationRadiansProperty);
        set => SetValue(RotationRadiansProperty, value);
    }

    public static readonly DependencyProperty RotationRadiansProperty =
        DependencyProperty.Register(nameof(RotationRadians), typeof(double), typeof(RouletteWheelControl),
            new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public bool PointerFlexEnabled
    {
        get => (bool)GetValue(PointerFlexEnabledProperty);
        set => SetValue(PointerFlexEnabledProperty, value);
    }

    public static readonly DependencyProperty PointerFlexEnabledProperty =
        DependencyProperty.Register(nameof(PointerFlexEnabled), typeof(bool), typeof(RouletteWheelControl),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

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
        UpdatePointerDynamics();
        DrawPointer(dc, center, radius);
    }

    private void UpdatePointerDynamics()
    {
        var now = DateTime.UtcNow;
        if (!PointerFlexEnabled)
        {
            _smoothedAngularSpeed = 0;
            _pointerFlexX = 0;
            _pointerFlexVelocityX = 0;
            _lastPointerRenderAt = now;
            _lastRotationRadians = RotationRadians;
            return;
        }

        if (_lastPointerRenderAt == DateTime.MinValue)
        {
            _lastPointerRenderAt = now;
            _lastRotationRadians = RotationRadians;
            return;
        }

        var dt = (now - _lastPointerRenderAt).TotalSeconds;
        if (dt <= 0 || dt > 0.2)
        {
            _lastPointerRenderAt = now;
            _lastRotationRadians = RotationRadians;
            return;
        }

        var delta = RotationRadians - _lastRotationRadians;
        delta = NormalizeSignedAngle(delta);
        var rawSpeed = delta / dt;
        _smoothedAngularSpeed = (_smoothedAngularSpeed * 0.8) + (rawSpeed * 0.2);
        // Spring-damper to create a "snapped/flicked" recoil feel.
        var targetFlex = Math.Sign(_smoothedAngularSpeed) * Math.Min(14, Math.Abs(_smoothedAngularSpeed) * 1.3);
        var stiffness = 95.0;
        var damping = 18.0;
        var accel = (targetFlex - _pointerFlexX) * stiffness - (_pointerFlexVelocityX * damping);
        _pointerFlexVelocityX += accel * dt;
        _pointerFlexX += _pointerFlexVelocityX * dt;

        _lastPointerRenderAt = now;
        _lastRotationRadians = RotationRadians;
    }

    private void DrawPointer(DrawingContext dc, Point center, double radius)
    {
        var topY = center.Y - radius;
        var stemLength = radius * 0.12;
        // Position pointer so it straddles the wheel boundary: half outside, half inside.
        var mountY = topY - (stemLength * 0.5);
        var joint1Y = mountY + stemLength * 0.38;
        var joint2Y = mountY + stemLength * 0.66;
        var joint3Y = mountY + stemLength * 0.86;
        var tipY = topY + (stemLength * 0.5);

        var speed = _smoothedAngularSpeed;
        var speedAbs = Math.Abs(speed);
        var flexAmount = Math.Min(10, speedAbs * 0.8);
        // Tip flex: spring recoil + small speed contribution.
        var tipShiftX = _pointerFlexX + (Math.Sign(speed) * flexAmount * 0.35);
        // Add slight vibration while spinning so the tip doesn't look rigid.
        var flutter = Math.Sin(DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond * 22) * Math.Min(1.6, speedAbs * 0.18);
        tipShiftX += flutter;

        var mountHalf = 4.8;
        var joint1Half = 3.1;
        var joint2Half = 2.0;
        var joint3Half = 1.0;
        var bend1 = tipShiftX * 0.22;
        var bend2 = tipShiftX * 0.48;
        var bend3 = tipShiftX * 0.78;
        var tip = new Point(center.X + tipShiftX, tipY);

        var pointer = new StreamGeometry();
        using (var ctx = pointer.Open())
        {
            ctx.BeginFigure(new Point(center.X - mountHalf, mountY), true, true);
            ctx.LineTo(new Point(center.X + mountHalf, mountY), true, false);
            ctx.LineTo(new Point(center.X + bend1 + joint1Half, joint1Y), true, false);
            ctx.LineTo(new Point(center.X + bend2 + joint2Half, joint2Y), true, false);
            ctx.LineTo(new Point(center.X + bend3 + joint3Half, joint3Y), true, false);
            ctx.LineTo(tip, true, false);
            ctx.LineTo(new Point(center.X + bend3 - joint3Half, joint3Y), true, false);
            ctx.LineTo(new Point(center.X + bend2 - joint2Half, joint2Y), true, false);
            ctx.LineTo(new Point(center.X + bend1 - joint1Half, joint1Y), true, false);
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

    private static double NormalizeSignedAngle(double angle)
    {
        var tau = Math.PI * 2;
        var normalized = angle % tau;
        if (normalized > Math.PI)
        {
            normalized -= tau;
        }
        else if (normalized < -Math.PI)
        {
            normalized += tau;
        }

        return normalized;
    }

    private sealed record WheelSegment(string Name, double Start, double End, SolidColorBrush Brush);
}
