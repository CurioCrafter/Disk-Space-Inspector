using System.Collections;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using DiskSpaceInspector.App.ViewModels;

namespace DiskSpaceInspector.App.Controls;

public sealed class SunburstView : FrameworkElement
{
    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(
            nameof(ItemsSource),
            typeof(IEnumerable),
            typeof(SunburstView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SegmentSelectedCommandProperty =
        DependencyProperty.Register(
            nameof(SegmentSelectedCommand),
            typeof(ICommand),
            typeof(SunburstView),
            new PropertyMetadata(null));

    private static readonly Dictionary<string, Color> Palette = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Folder"] = Color.FromRgb(47, 111, 158),
        ["Drive"] = Color.FromRgb(46, 196, 182),
        ["Video"] = Color.FromRgb(167, 90, 123),
        ["Image"] = Color.FromRgb(201, 135, 47),
        ["Archive"] = Color.FromRgb(123, 101, 179),
        ["Installer"] = Color.FromRgb(196, 86, 77),
        ["System"] = Color.FromRgb(102, 113, 122),
        ["Code"] = Color.FromRgb(41, 154, 168),
        ["Temporary"] = Color.FromRgb(184, 138, 37),
        ["Link"] = Color.FromRgb(126, 150, 82),
        ["Other"] = Color.FromRgb(88, 101, 109)
    };

    private List<SunburstSegmentViewModel> _segments = [];

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public ICommand? SegmentSelectedCommand
    {
        get => (ICommand?)GetValue(SegmentSelectedCommandProperty);
        set => SetValue(SegmentSelectedCommandProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        drawingContext.DrawRectangle(new SolidColorBrush(Color.FromRgb(9, 13, 16)), null, new Rect(0, 0, ActualWidth, ActualHeight));
        _segments = ItemsSource?.Cast<SunburstSegmentViewModel>().ToList() ?? [];
        if (_segments.Count == 0)
        {
            DrawCenteredText(drawingContext, "No hierarchy loaded", 13, Color.FromRgb(127, 140, 146));
            return;
        }

        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var radius = Math.Max(1, Math.Min(ActualWidth, ActualHeight) / 2 - 14);
        foreach (var segment in _segments)
        {
            var geometry = CreateSegmentGeometry(center, radius, segment);
            var color = GetColor(segment.ColorKey, segment.Depth);
            drawingContext.DrawGeometry(
                new SolidColorBrush(color),
                new Pen(new SolidColorBrush(Color.FromArgb(185, 9, 13, 16)), 1),
                geometry);
        }

        drawingContext.DrawEllipse(new SolidColorBrush(Color.FromRgb(19, 27, 32)), null, center, radius * 0.16, radius * 0.16);
        DrawCenteredText(drawingContext, "Disk Space", 12, Color.FromRgb(238, 243, 245));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var segment = HitTestSegment(e.GetPosition(this));
        ToolTip = segment is null ? null : $"{segment.Label}\n{segment.SizeDisplay}\n{segment.Path}";
        Cursor = segment?.NodeId is null ? Cursors.Arrow : Cursors.Hand;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        var segment = HitTestSegment(e.GetPosition(this));
        if (segment is not null && SegmentSelectedCommand?.CanExecute(segment) == true)
        {
            SegmentSelectedCommand.Execute(segment);
        }
    }

    private SunburstSegmentViewModel? HitTestSegment(Point point)
    {
        if (_segments.Count == 0 || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return null;
        }

        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var dx = point.X - center.X;
        var dy = point.Y - center.Y;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        var radius = Math.Max(1, Math.Min(ActualWidth, ActualHeight) / 2 - 14);
        var normalizedRadius = distance / radius;
        var angle = Math.Atan2(dy, dx) * 180 / Math.PI;

        return _segments.LastOrDefault(segment =>
            normalizedRadius >= segment.InnerRadius &&
            normalizedRadius <= segment.OuterRadius &&
            AngleContains(segment.StartAngle, segment.SweepAngle, angle));
    }

    private static bool AngleContains(double startAngle, double sweepAngle, double angle)
    {
        var normalizedStart = Normalize(startAngle);
        var normalizedEnd = Normalize(startAngle + sweepAngle);
        var normalizedAngle = Normalize(angle);
        return normalizedStart <= normalizedEnd
            ? normalizedAngle >= normalizedStart && normalizedAngle <= normalizedEnd
            : normalizedAngle >= normalizedStart || normalizedAngle <= normalizedEnd;
    }

    private static double Normalize(double angle)
    {
        angle %= 360;
        return angle < 0 ? angle + 360 : angle;
    }

    private static Geometry CreateSegmentGeometry(Point center, double radius, SunburstSegmentViewModel segment)
    {
        var inner = Math.Max(0, segment.InnerRadius * radius);
        var outer = Math.Max(inner + 0.5, segment.OuterRadius * radius);
        var start = segment.StartAngle;
        var end = segment.StartAngle + Math.Max(0.2, segment.SweepAngle);
        var outerStart = PointOnCircle(center, outer, start);
        var outerEnd = PointOnCircle(center, outer, end);
        var innerEnd = PointOnCircle(center, inner, end);
        var innerStart = PointOnCircle(center, inner, start);
        var large = Math.Abs(segment.SweepAngle) > 180;

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(outerStart, isFilled: true, isClosed: true);
            context.ArcTo(outerEnd, new Size(outer, outer), 0, large, SweepDirection.Clockwise, isStroked: true, isSmoothJoin: true);
            context.LineTo(innerEnd, isStroked: true, isSmoothJoin: true);
            context.ArcTo(innerStart, new Size(inner, inner), 0, large, SweepDirection.Counterclockwise, isStroked: true, isSmoothJoin: true);
        }

        geometry.Freeze();
        return geometry;
    }

    private static Point PointOnCircle(Point center, double radius, double angleDegrees)
    {
        var radians = angleDegrees * Math.PI / 180;
        return new Point(center.X + Math.Cos(radians) * radius, center.Y + Math.Sin(radians) * radius);
    }

    private static Color GetColor(string key, int depth)
    {
        var color = Palette.TryGetValue(key, out var known) ? known : Palette["Other"];
        var factor = Math.Max(0.72, 1 - depth * 0.07);
        return Color.FromRgb(
            (byte)Math.Clamp(color.R * factor, 0, 255),
            (byte)Math.Clamp(color.G * factor, 0, 255),
            (byte)Math.Clamp(color.B * factor, 0, 255));
    }

    private void DrawCenteredText(DrawingContext drawingContext, string value, double size, Color color)
    {
        var text = new FormattedText(
            value,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            size,
            new SolidColorBrush(color),
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        drawingContext.DrawText(text, new Point((ActualWidth - text.Width) / 2, (ActualHeight - text.Height) / 2));
    }
}
