using System.Collections;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using DiskSpaceInspector.App.ViewModels;

namespace DiskSpaceInspector.App.Controls;

public sealed class TreemapView : FrameworkElement
{
    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(
            nameof(ItemsSource),
            typeof(IEnumerable),
            typeof(TreemapView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TileSelectedCommandProperty =
        DependencyProperty.Register(
            nameof(TileSelectedCommand),
            typeof(ICommand),
            typeof(TreemapView),
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

    private List<TreemapTileViewModel> _tiles = [];

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public ICommand? TileSelectedCommand
    {
        get => (ICommand?)GetValue(TileSelectedCommandProperty);
        set => SetValue(TileSelectedCommandProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        drawingContext.DrawRectangle(new SolidColorBrush(Color.FromRgb(9, 13, 16)), null, new Rect(0, 0, ActualWidth, ActualHeight));
        _tiles = ItemsSource?.Cast<TreemapTileViewModel>().ToList() ?? [];
        if (_tiles.Count == 0)
        {
            DrawCenteredText(drawingContext, "No scan selected", 14, Color.FromRgb(127, 140, 146));
            return;
        }

        var sourceWidth = Math.Max(1, _tiles.Max(t => t.X + t.Width));
        var sourceHeight = Math.Max(1, _tiles.Max(t => t.Y + t.Height));
        var scaleX = ActualWidth / sourceWidth;
        var scaleY = ActualHeight / sourceHeight;

        foreach (var tile in _tiles)
        {
            var rect = ToScreenRect(tile, scaleX, scaleY);
            if (rect.Width < 0.5 || rect.Height < 0.5)
            {
                continue;
            }

            var color = GetTileColor(tile.ColorKey);
            var fill = new LinearGradientBrush(
                Lighten(color, 1.10),
                Darken(color, 0.82),
                new Point(0, 0),
                new Point(1, 1));
            var border = new Pen(new SolidColorBrush(Color.FromArgb(170, 9, 13, 16)), 1);
            var rounded = rect;
            rounded.Inflate(-1, -1);
            drawingContext.DrawRoundedRectangle(fill, border, rounded, 3, 3);

            if (rect.Width >= 58 && rect.Height >= 26)
            {
                var text = new FormattedText(
                    tile.Label,
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"),
                    11,
                    new SolidColorBrush(GetReadableTextColor(color)),
                    VisualTreeHelper.GetDpi(this).PixelsPerDip)
                {
                    MaxTextWidth = Math.Max(8, rect.Width - 8),
                    MaxTextHeight = Math.Max(8, rect.Height - 8),
                    Trimming = TextTrimming.CharacterEllipsis
                };
                drawingContext.DrawText(text, new Point(rect.X + 4, rect.Y + 4));
            }
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var tile = HitTestTile(e.GetPosition(this));
        ToolTip = tile is null ? null : $"{tile.Label}\n{tile.SizeDisplay}\n{tile.Path}";
        Cursor = tile?.NodeId is null ? Cursors.Arrow : Cursors.Hand;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        var tile = HitTestTile(e.GetPosition(this));
        if (tile is not null && TileSelectedCommand?.CanExecute(tile) == true)
        {
            TileSelectedCommand.Execute(tile);
        }
    }

    private TreemapTileViewModel? HitTestTile(Point point)
    {
        if (_tiles.Count == 0 || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return null;
        }

        var sourceWidth = Math.Max(1, _tiles.Max(t => t.X + t.Width));
        var sourceHeight = Math.Max(1, _tiles.Max(t => t.Y + t.Height));
        var scaleX = ActualWidth / sourceWidth;
        var scaleY = ActualHeight / sourceHeight;

        return _tiles
            .Select(t => new { Tile = t, Rect = ToScreenRect(t, scaleX, scaleY) })
            .FirstOrDefault(x => x.Rect.Contains(point))?.Tile;
    }

    private static Rect ToScreenRect(TreemapTileViewModel tile, double scaleX, double scaleY)
    {
        return new Rect(tile.X * scaleX, tile.Y * scaleY, tile.Width * scaleX, tile.Height * scaleY);
    }

    private static Color GetTileColor(string key)
    {
        return Palette.TryGetValue(key, out var color) ? color : Palette["Other"];
    }

    private static Color Lighten(Color color, double factor)
    {
        return Color.FromRgb(
            (byte)Math.Clamp(color.R * factor, 0, 255),
            (byte)Math.Clamp(color.G * factor, 0, 255),
            (byte)Math.Clamp(color.B * factor, 0, 255));
    }

    private static Color Darken(Color color, double factor)
    {
        return Lighten(color, factor);
    }

    private static Color GetReadableTextColor(Color color)
    {
        var luminance = 0.2126 * ToLinear(color.R) + 0.7152 * ToLinear(color.G) + 0.0722 * ToLinear(color.B);
        return luminance > 0.42 ? Color.FromRgb(9, 13, 16) : Color.FromRgb(238, 243, 245);
    }

    private static double ToLinear(byte value)
    {
        var normalized = value / 255.0;
        return normalized <= 0.03928 ? normalized / 12.92 : Math.Pow((normalized + 0.055) / 1.055, 2.4);
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
