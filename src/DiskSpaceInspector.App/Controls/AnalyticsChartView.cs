using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using DiskSpaceInspector.Core.Models;

namespace DiskSpaceInspector.App.Controls;

public sealed class AnalyticsChartView : FrameworkElement
{
    public static readonly DependencyProperty ChartProperty =
        DependencyProperty.Register(
            nameof(Chart),
            typeof(ChartDefinition),
            typeof(AnalyticsChartView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ChartSelectedCommandProperty =
        DependencyProperty.Register(
            nameof(ChartSelectedCommand),
            typeof(ICommand),
            typeof(AnalyticsChartView),
            new PropertyMetadata(null));

    private readonly List<HitRegion> _hitRegions = [];

    public ChartDefinition? Chart
    {
        get => (ChartDefinition?)GetValue(ChartProperty);
        set => SetValue(ChartProperty, value);
    }

    public ICommand? ChartSelectedCommand
    {
        get => (ICommand?)GetValue(ChartSelectedCommandProperty);
        set => SetValue(ChartSelectedCommandProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        _hitRegions.Clear();
        drawingContext.DrawRectangle(new SolidColorBrush(Color.FromRgb(9, 13, 16)), null, new Rect(0, 0, ActualWidth, ActualHeight));

        if (Chart is null || !HasData(Chart))
        {
            DrawCenteredText(drawingContext, "No analytics yet", 13, Palette["Muted"]);
            return;
        }

        var bounds = new Rect(8, 8, Math.Max(1, ActualWidth - 16), Math.Max(1, ActualHeight - 16));
        switch (Chart.Kind)
        {
            case VisualChartKind.KpiStrip:
                DrawKpis(drawingContext, bounds, Chart);
                break;
            case VisualChartKind.RankBar:
            case VisualChartKind.Funnel:
            case VisualChartKind.Waterfall:
                DrawRankBars(drawingContext, bounds, Chart);
                break;
            case VisualChartKind.StackedBar:
                DrawStackedBars(drawingContext, bounds, Chart);
                break;
            case VisualChartKind.Curve:
            case VisualChartKind.Timeline:
                DrawCurve(drawingContext, bounds, Chart);
                break;
            case VisualChartKind.Heatmap:
            case VisualChartKind.Matrix:
            case VisualChartKind.CalendarHeatmap:
                DrawHeatmap(drawingContext, bounds, Chart);
                break;
            case VisualChartKind.Scatter:
                DrawScatter(drawingContext, bounds, Chart);
                break;
            case VisualChartKind.Radar:
                DrawRadar(drawingContext, bounds, Chart);
                break;
            case VisualChartKind.RelationshipFlow:
                DrawFlows(drawingContext, bounds, Chart);
                break;
            case VisualChartKind.BubblePack:
                DrawBubbles(drawingContext, bounds, Chart);
                break;
            case VisualChartKind.Donut:
                DrawDonut(drawingContext, bounds, Chart);
                break;
            default:
                DrawRankBars(drawingContext, bounds, Chart);
                break;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var hit = HitTestRegion(e.GetPosition(this));
        ToolTip = hit?.ToolTip;
        Cursor = hit?.Payload is null ? Cursors.Arrow : Cursors.Hand;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        var hit = HitTestRegion(e.GetPosition(this));
        if (hit?.Payload is not null && ChartSelectedCommand?.CanExecute(hit.Payload) == true)
        {
            ChartSelectedCommand.Execute(hit.Payload);
        }
    }

    private void DrawKpis(DrawingContext dc, Rect bounds, ChartDefinition chart)
    {
        var metrics = chart.Metrics.Take(6).ToList();
        var columns = Math.Max(1, metrics.Count);
        var width = (bounds.Width - (columns - 1) * 8) / columns;
        for (var i = 0; i < metrics.Count; i++)
        {
            var metric = metrics[i];
            var rect = new Rect(bounds.X + i * (width + 8), bounds.Y, width, bounds.Height);
            DrawRounded(dc, rect, Color.FromRgb(16, 23, 27), Color.FromRgb(43, 55, 61));
            DrawText(dc, metric.Label, rect.X + 10, rect.Y + 10, 11, Palette["Muted"], rect.Width - 20);
            DrawText(dc, metric.Value, rect.X + 10, rect.Y + 34, 20, GetColor(metric.ColorKey), rect.Width - 20, FontWeights.SemiBold);
            DrawText(dc, metric.Detail, rect.X + 10, rect.Y + 64, 11, Palette["Secondary"], rect.Width - 20);
        }
    }

    private void DrawRankBars(DrawingContext dc, Rect bounds, ChartDefinition chart)
    {
        var points = chart.Points.Take(14).ToList();
        var max = Math.Max(1, points.Max(p => Math.Abs(p.Y)));
        var rowHeight = Math.Max(18, bounds.Height / Math.Max(1, points.Count));
        for (var i = 0; i < points.Count; i++)
        {
            var point = points[i];
            var y = bounds.Y + i * rowHeight;
            var labelWidth = Math.Min(130, bounds.Width * 0.34);
            var barX = bounds.X + labelWidth + 8;
            var barWidth = Math.Max(4, (bounds.Width - labelWidth - 76) * Math.Abs(point.Y) / max);
            var barRect = new Rect(barX, y + 5, barWidth, Math.Max(7, rowHeight - 10));
            DrawText(dc, point.Label, bounds.X, y + 4, 11, Palette["Secondary"], labelWidth);
            DrawRounded(dc, new Rect(barX, y + rowHeight / 2 - 4, bounds.Width - labelWidth - 76, 8), Color.FromRgb(12, 20, 23), Color.FromRgb(12, 20, 23), 4);
            DrawRounded(dc, barRect, GetColor(point.ColorKey), GetColor(point.ColorKey), 4);
            DrawText(dc, point.DisplayValue, bounds.Right - 64, y + 4, 11, Palette["Muted"], 64);
            AddHit(barRect, point, point.Detail);
        }
    }

    private void DrawStackedBars(DrawingContext dc, Rect bounds, ChartDefinition chart)
    {
        var series = chart.Series.Count > 0
            ? chart.Series.Take(8).ToList()
            : [new ChartSeries { Name = chart.Title, Points = chart.Points }];
        var rowHeight = Math.Max(24, bounds.Height / Math.Max(1, series.Count));
        for (var row = 0; row < series.Count; row++)
        {
            var item = series[row];
            var points = item.Points.Take(8).ToList();
            var total = Math.Max(1, points.Sum(p => Math.Abs(p.Y)));
            var y = bounds.Y + row * rowHeight;
            var labelWidth = Math.Min(118, bounds.Width * 0.3);
            DrawText(dc, item.Name, bounds.X, y + 5, 11, Palette["Secondary"], labelWidth);
            var x = bounds.X + labelWidth + 8;
            foreach (var point in points)
            {
                var width = Math.Max(2, (bounds.Width - labelWidth - 12) * Math.Abs(point.Y) / total);
                var rect = new Rect(x, y + 7, width, Math.Max(10, rowHeight - 14));
                DrawRounded(dc, rect, GetColor(point.ColorKey), GetColor(point.ColorKey), 3);
                AddHit(rect, point, $"{point.Label}: {point.DisplayValue}");
                x += width;
            }
        }
    }

    private void DrawCurve(DrawingContext dc, Rect bounds, ChartDefinition chart)
    {
        var points = chart.Points.Take(80).ToList();
        var maxY = Math.Max(1, points.Max(p => Math.Abs(p.Y)));
        DrawGrid(dc, bounds);
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            for (var i = 0; i < points.Count; i++)
            {
                var x = bounds.X + (points.Count == 1 ? 0.5 : i / (double)(points.Count - 1)) * bounds.Width;
                var y = bounds.Bottom - (Math.Abs(points[i].Y) / maxY) * bounds.Height;
                if (i == 0)
                {
                    context.BeginFigure(new Point(x, y), false, false);
                }
                else
                {
                    context.LineTo(new Point(x, y), true, true);
                }

                AddHit(new Rect(x - 5, y - 5, 10, 10), points[i], points[i].Detail);
            }
        }

        geometry.Freeze();
        dc.DrawGeometry(null, new Pen(new SolidColorBrush(Palette["Accent"]), 2.2), geometry);
        foreach (var point in points.TakeLast(Math.Min(10, points.Count)))
        {
            var i = points.IndexOf(point);
            var x = bounds.X + (points.Count == 1 ? 0.5 : i / (double)(points.Count - 1)) * bounds.Width;
            var y = bounds.Bottom - (Math.Abs(point.Y) / maxY) * bounds.Height;
            dc.DrawEllipse(new SolidColorBrush(GetColor(point.ColorKey)), null, new Point(x, y), 3, 3);
        }
    }

    private void DrawHeatmap(DrawingContext dc, Rect bounds, ChartDefinition chart)
    {
        var cells = chart.Cells.Take(160).ToList();
        var rows = cells.Select(c => c.Row).Distinct().Take(14).ToList();
        var columns = cells.Select(c => c.Column).Distinct().Take(12).ToList();
        var max = Math.Max(1, cells.Max(c => c.Value));
        var labelWidth = Math.Min(98, bounds.Width * 0.25);
        var top = 20d;
        var cellWidth = Math.Max(8, (bounds.Width - labelWidth - 6) / Math.Max(1, columns.Count));
        var cellHeight = Math.Max(8, (bounds.Height - top) / Math.Max(1, rows.Count));

        for (var c = 0; c < columns.Count; c++)
        {
            DrawText(dc, columns[c], bounds.X + labelWidth + c * cellWidth, bounds.Y, 10, Palette["Muted"], cellWidth);
        }

        for (var r = 0; r < rows.Count; r++)
        {
            DrawText(dc, rows[r], bounds.X, bounds.Y + top + r * cellHeight + 2, 10, Palette["Secondary"], labelWidth - 4);
            for (var c = 0; c < columns.Count; c++)
            {
                var cell = cells.FirstOrDefault(x => x.Row == rows[r] && x.Column == columns[c]);
                var rect = new Rect(bounds.X + labelWidth + c * cellWidth + 2, bounds.Y + top + r * cellHeight + 2, cellWidth - 4, cellHeight - 4);
                var intensity = cell is null ? 0 : Math.Clamp(cell.Value / max, 0.08, 1);
                DrawRounded(dc, rect, Blend(Color.FromRgb(16, 23, 27), GetColor(cell?.ColorKey ?? "Other"), intensity), Color.FromRgb(43, 55, 61), 2);
                if (cell is not null)
                {
                    AddHit(rect, cell, cell.Detail);
                }
            }
        }
    }

    private void DrawScatter(DrawingContext dc, Rect bounds, ChartDefinition chart)
    {
        var points = chart.Points.Take(160).ToList();
        var maxX = Math.Max(1, points.Max(p => Math.Abs(p.X)));
        var maxY = Math.Max(1, points.Max(p => Math.Abs(p.Y)));
        DrawGrid(dc, bounds);
        foreach (var point in points)
        {
            var x = bounds.X + Math.Abs(point.X) / maxX * bounds.Width;
            var y = bounds.Bottom - Math.Abs(point.Y) / maxY * bounds.Height;
            var radius = 4 + Math.Min(14, Math.Log10(Math.Max(1, point.SizeBytes)) * 1.4);
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(210, GetColor(point.ColorKey).R, GetColor(point.ColorKey).G, GetColor(point.ColorKey).B)), new Pen(new SolidColorBrush(Color.FromRgb(9, 13, 16)), 1), new Point(x, y), radius, radius);
            AddHit(new Rect(x - radius, y - radius, radius * 2, radius * 2), point, point.Detail);
        }
    }

    private void DrawRadar(DrawingContext dc, Rect bounds, ChartDefinition chart)
    {
        var points = chart.Points.Take(8).ToList();
        var center = new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
        var radius = Math.Min(bounds.Width, bounds.Height) * 0.42;
        for (var ring = 1; ring <= 4; ring++)
        {
            dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromRgb(35, 48, 56)), 1), center, radius * ring / 4, radius * ring / 4);
        }

        var figure = new StreamGeometry();
        using (var context = figure.Open())
        {
            for (var i = 0; i < points.Count; i++)
            {
                var angle = -Math.PI / 2 + i * Math.PI * 2 / points.Count;
                var axis = new Point(center.X + Math.Cos(angle) * radius, center.Y + Math.Sin(angle) * radius);
                dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(35, 48, 56)), 1), center, axis);
                DrawText(dc, points[i].Label, axis.X - 36, axis.Y - 8, 10, Palette["Secondary"], 72);
                var valueRadius = radius * Math.Clamp(Math.Max(points[i].Y, points[i].Score), 0.05, 1);
                var valuePoint = new Point(center.X + Math.Cos(angle) * valueRadius, center.Y + Math.Sin(angle) * valueRadius);
                if (i == 0)
                {
                    context.BeginFigure(valuePoint, true, true);
                }
                else
                {
                    context.LineTo(valuePoint, true, true);
                }
                AddHit(new Rect(valuePoint.X - 6, valuePoint.Y - 6, 12, 12), points[i], points[i].Detail);
            }
        }

        figure.Freeze();
        dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(90, 46, 196, 182)), new Pen(new SolidColorBrush(Palette["Accent"]), 2), figure);
    }

    private void DrawFlows(DrawingContext dc, Rect bounds, ChartDefinition chart)
    {
        var flows = chart.Flows.Take(12).ToList();
        var left = flows.Select(f => f.Source).Distinct().ToList();
        var right = flows.Select(f => f.Target).Distinct().ToList();
        var max = Math.Max(1, flows.Max(f => f.Value));
        var leftPoints = NodeSlots(left, bounds.Left + 18, bounds.Top + 18, bounds.Height - 36);
        var rightPoints = NodeSlots(right, bounds.Right - 18, bounds.Top + 18, bounds.Height - 36);

        foreach (var flow in flows)
        {
            var start = leftPoints[flow.Source];
            var end = rightPoints[flow.Target];
            var thickness = 1.5 + 8 * flow.Value / max;
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                context.BeginFigure(start, false, false);
                context.BezierTo(new Point(bounds.X + bounds.Width * 0.38, start.Y), new Point(bounds.X + bounds.Width * 0.62, end.Y), end, true, true);
            }
            geometry.Freeze();
            dc.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(210, GetColor(flow.ColorKey).R, GetColor(flow.ColorKey).G, GetColor(flow.ColorKey).B)), thickness), geometry);
            AddHit(new Rect(Math.Min(start.X, end.X), Math.Min(start.Y, end.Y) - thickness, Math.Abs(end.X - start.X), Math.Abs(end.Y - start.Y) + thickness * 2), flow, $"{flow.Source} -> {flow.Target}\n{flow.Detail}\n{flow.Confidence:P0}");
        }

        foreach (var entry in leftPoints)
        {
            DrawText(dc, entry.Key, bounds.Left + 2, entry.Value.Y - 7, 10, Palette["Secondary"], bounds.Width * 0.34);
        }
        foreach (var entry in rightPoints)
        {
            DrawText(dc, entry.Key, bounds.Right - bounds.Width * 0.35, entry.Value.Y - 7, 10, Palette["Secondary"], bounds.Width * 0.35);
        }
    }

    private void DrawBubbles(DrawingContext dc, Rect bounds, ChartDefinition chart)
    {
        var points = chart.Points.Take(24).ToList();
        var max = Math.Max(1, points.Max(p => p.Y));
        var columns = Math.Max(3, (int)Math.Ceiling(Math.Sqrt(points.Count)));
        var cellWidth = bounds.Width / columns;
        var rows = (int)Math.Ceiling(points.Count / (double)columns);
        var cellHeight = bounds.Height / Math.Max(1, rows);
        for (var i = 0; i < points.Count; i++)
        {
            var point = points[i];
            var column = i % columns;
            var row = i / columns;
            var center = new Point(bounds.X + column * cellWidth + cellWidth / 2, bounds.Y + row * cellHeight + cellHeight / 2);
            var radius = Math.Max(10, Math.Min(cellWidth, cellHeight) * (0.18 + 0.32 * Math.Sqrt(Math.Abs(point.Y) / max)));
            dc.DrawEllipse(new SolidColorBrush(GetColor(point.ColorKey)), new Pen(new SolidColorBrush(Color.FromRgb(9, 13, 16)), 1), center, radius, radius);
            DrawText(dc, point.Label, center.X - radius + 4, center.Y - 6, 10, Readable(GetColor(point.ColorKey)), radius * 2 - 8);
            AddHit(new Rect(center.X - radius, center.Y - radius, radius * 2, radius * 2), point, point.Detail);
        }
    }

    private void DrawDonut(DrawingContext dc, Rect bounds, ChartDefinition chart)
    {
        var points = chart.Points.Take(5).ToList();
        var columns = Math.Max(1, points.Count);
        var cellWidth = bounds.Width / columns;
        for (var i = 0; i < points.Count; i++)
        {
            var point = points[i];
            var center = new Point(bounds.X + i * cellWidth + cellWidth / 2, bounds.Y + bounds.Height * 0.42);
            var radius = Math.Min(cellWidth, bounds.Height) * 0.28;
            dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromRgb(24, 36, 42)), 8), center, radius, radius);
            var sweep = Math.Clamp(point.Fraction, 0, 1) * 360;
            DrawArc(dc, center, radius, -90, sweep, new Pen(new SolidColorBrush(GetColor(point.ColorKey)), 8));
            DrawText(dc, $"{point.Fraction:P0}", center.X - 24, center.Y - 9, 13, Palette["Primary"], 48, FontWeights.SemiBold);
            DrawText(dc, point.Label, bounds.X + i * cellWidth + 4, bounds.Bottom - 42, 11, Palette["Secondary"], cellWidth - 8);
            DrawText(dc, point.DisplayValue, bounds.X + i * cellWidth + 4, bounds.Bottom - 22, 11, Palette["Muted"], cellWidth - 8);
            AddHit(new Rect(center.X - radius, center.Y - radius, radius * 2, radius * 2), point, point.Detail);
        }
    }

    private static Dictionary<string, Point> NodeSlots(IReadOnlyList<string> labels, double x, double y, double height)
    {
        var result = new Dictionary<string, Point>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < labels.Count; i++)
        {
            var slot = labels.Count == 1 ? y + height / 2 : y + i * height / (labels.Count - 1);
            result[labels[i]] = new Point(x, slot);
        }
        return result;
    }

    private void DrawGrid(DrawingContext dc, Rect bounds)
    {
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(28, 40, 47)), 1);
        for (var i = 0; i <= 4; i++)
        {
            var y = bounds.Y + i * bounds.Height / 4;
            dc.DrawLine(pen, new Point(bounds.X, y), new Point(bounds.Right, y));
        }
    }

    private void DrawArc(DrawingContext dc, Point center, double radius, double startAngle, double sweepAngle, Pen pen)
    {
        if (sweepAngle <= 0)
        {
            return;
        }

        var start = PointOnCircle(center, radius, startAngle);
        var end = PointOnCircle(center, radius, startAngle + Math.Min(359.9, sweepAngle));
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(start, false, false);
            context.ArcTo(end, new Size(radius, radius), 0, sweepAngle > 180, SweepDirection.Clockwise, true, true);
        }
        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);
    }

    private static Point PointOnCircle(Point center, double radius, double angleDegrees)
    {
        var radians = angleDegrees * Math.PI / 180;
        return new Point(center.X + Math.Cos(radians) * radius, center.Y + Math.Sin(radians) * radius);
    }

    private void DrawRounded(DrawingContext dc, Rect rect, Color fill, Color stroke, double radius = 5)
    {
        dc.DrawRoundedRectangle(new SolidColorBrush(fill), new Pen(new SolidColorBrush(stroke), 1), rect, radius, radius);
    }

    private void DrawText(DrawingContext dc, string text, double x, double y, double size, Color color, double maxWidth, FontWeight? weight = null)
    {
        if (maxWidth <= 2 || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var formatted = new FormattedText(
            text,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, weight ?? FontWeights.Normal, FontStretches.Normal),
            size,
            new SolidColorBrush(color),
            VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            MaxTextWidth = maxWidth,
            MaxTextHeight = size * 1.45,
            Trimming = TextTrimming.CharacterEllipsis
        };
        dc.DrawText(formatted, new Point(x, y));
    }

    private void DrawCenteredText(DrawingContext dc, string value, double size, Color color)
    {
        var text = new FormattedText(
            value,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            size,
            new SolidColorBrush(color),
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(text, new Point((ActualWidth - text.Width) / 2, (ActualHeight - text.Height) / 2));
    }

    private void AddHit(Rect rect, object payload, string tooltip)
    {
        _hitRegions.Add(new HitRegion(rect, payload, tooltip));
    }

    private HitRegion? HitTestRegion(Point point)
    {
        return _hitRegions.LastOrDefault(region => region.Bounds.Contains(point));
    }

    private static bool HasData(ChartDefinition chart)
    {
        return chart.Points.Count > 0 || chart.Cells.Count > 0 || chart.Flows.Count > 0 || chart.Series.Count > 0 || chart.Metrics.Count > 0;
    }

    private static Color Blend(Color from, Color to, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromRgb(
            (byte)(from.R + (to.R - from.R) * amount),
            (byte)(from.G + (to.G - from.G) * amount),
            (byte)(from.B + (to.B - from.B) * amount));
    }

    private static Color Readable(Color color)
    {
        var luminance = 0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B;
        return luminance > 145 ? Color.FromRgb(9, 13, 16) : Palette["Primary"];
    }

    private static Color GetColor(string key)
    {
        return Palette.TryGetValue(key, out var color) ? color : Palette["Other"];
    }

    private static readonly Dictionary<string, Color> Palette = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Primary"] = Color.FromRgb(238, 243, 245),
        ["Secondary"] = Color.FromRgb(184, 195, 201),
        ["Muted"] = Color.FromRgb(127, 140, 146),
        ["Accent"] = Color.FromRgb(46, 196, 182),
        ["Drive"] = Color.FromRgb(46, 196, 182),
        ["Folder"] = Color.FromRgb(47, 111, 158),
        ["Video"] = Color.FromRgb(167, 90, 123),
        ["Image"] = Color.FromRgb(201, 135, 47),
        ["Archive"] = Color.FromRgb(123, 101, 179),
        ["Installer"] = Color.FromRgb(196, 86, 77),
        ["System"] = Color.FromRgb(102, 113, 122),
        ["Code"] = Color.FromRgb(41, 154, 168),
        ["Temporary"] = Color.FromRgb(184, 138, 37),
        ["Link"] = Color.FromRgb(126, 150, 82),
        ["Safe"] = Color.FromRgb(104, 195, 107),
        ["Review"] = Color.FromRgb(242, 184, 75),
        ["Blocked"] = Color.FromRgb(240, 100, 85),
        ["Danger"] = Color.FromRgb(240, 100, 85),
        ["Other"] = Color.FromRgb(88, 101, 109)
    };

    private sealed record HitRegion(Rect Bounds, object Payload, string ToolTip);
}
