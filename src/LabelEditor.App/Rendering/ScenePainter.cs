using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using LabelEditor.Domain.Entries;
using LabelEditor.Domain.Geometry;
using LabelEditor.Domain.Text;
using LabelEditor.Rendering.Scene;

namespace LabelEditor.App.Rendering;

/// <summary>Maps layout units to device-independent pixels for one zoom level.</summary>
/// <param name="Scale">DIPs per layout unit.</param>
/// <param name="Origin">DIP position of the label's top-left corner.</param>
public readonly record struct ViewTransform(double Scale, Point Origin)
{
    /// <summary>DIPs per layout unit at a zoom factor (1 = physical size on a 96 DPI screen).</summary>
    public static double ScaleFor(double zoom, MeasurementUnit unit) =>
        zoom * 96.0 / (unit == MeasurementUnit.Inch ? 1.0 : LayoutUnits.MillimetresPerInch) / LayoutUnits.PerUnit;

    public Point ToView(int x, int y) => new(Origin.X + (x * Scale), Origin.Y + (y * Scale));

    public Point ToView(LabelPoint p) => ToView(p.X, p.Y);

    public Rect ToView(LabelRect r)
    {
        var n = r.Normalize();
        return new Rect(ToView(n.X, n.Y), new Size(n.Width * Scale, n.Height * Scale));
    }

    public LabelPoint ToLayout(Point p) => new((int)Math.Round((p.X - Origin.X) / Scale), (int)Math.Round((p.Y - Origin.Y) / Scale));
}

/// <summary>Paints a <see cref="LabelScene"/> onto an Avalonia drawing context, as close to print as possible.</summary>
public sealed class ScenePainter
{
    private static readonly IBrush Ink = Brushes.Black;
    private static readonly IPen FramePen = new Pen(new SolidColorBrush(Color.FromRgb(150, 150, 150)), 1, DashStyle.Dash);
    private static readonly IBrush WarningFill = new SolidColorBrush(Color.FromArgb(60, 255, 80, 0));
    private readonly Dictionary<string, Bitmap?> _bitmaps = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Draws design aids (frames around placeholders). Off for a clean print preview.</summary>
    public bool ShowDesignAids { get; set; } = true;

    /// <summary>The label paper colour; white surfaces on the label (corner logo) use it so they match a dimmed label.</summary>
    public IBrush Paper { get; set; } = Brushes.White;

    public void Paint(DrawingContext context, LabelScene scene, ViewTransform view)
    {
        foreach (var item in scene.Items)
        {
            switch (item)
            {
                case TextItem t: PaintText(context, t, view); break;
                case LinearBarcodeItem b: PaintLinearBarcode(context, b, view); break;
                case MatrixBarcodeItem m: PaintMatrix(context, m, view); break;
                case BarcodeErrorItem e: PaintError(context, e, view); break;
                case EllipseItem e: PaintEllipse(context, e, view); break;
                case LineItem l: context.DrawLine(new Pen(Ink, Math.Max(1, 200 * view.Scale)), view.ToView(l.Bounds.Start), view.ToView(l.Bounds.End)); break;
                case ImageItem i: PaintImage(context, i, view); break;
                case CornerLogoItem c: PaintCornerLogo(context, c, view); break;
                case PlaceholderItem p: PaintPlaceholder(context, view.ToView(p.Bounds), p.Caption); break;
            }
        }
    }

    public static Typeface TypefaceFor(string family, bool bold, bool italic) =>
        new(new FontFamily(family), italic ? FontStyle.Italic : FontStyle.Normal, bold ? FontWeight.Bold : FontWeight.Normal);

    /// <summary>
    /// Pushes a transform so that drawing in a (w × h) frame at the origin lands rotated inside <paramref name="box"/>.
    /// LEdit's escapement is counter-clockwise; screen rotation is clockwise.
    /// </summary>
    private static DrawingContext.PushedState PushRotatedFrame(DrawingContext context, Rect box, TextRotation rotation, out Size frame)
    {
        var degrees = rotation switch
        {
            TextRotation.Ccw90 => -90,
            TextRotation.Cw90 => 90,
            TextRotation.Rotate180 => 180,
            _ => 0,
        };
        frame = degrees is 90 or -90 ? new Size(box.Height, box.Width) : box.Size;
        var matrix = Matrix.CreateTranslation(-frame.Width / 2, -frame.Height / 2)
            * Matrix.CreateRotation(degrees * Math.PI / 180)
            * Matrix.CreateTranslation(box.Center.X, box.Center.Y);
        return context.PushTransform(matrix);
    }

    private static void PaintText(DrawingContext context, TextItem t, ViewTransform view)
    {
        if (t.Text.Length == 0)
        {
            return;
        }

        var box = view.ToView(t.Bounds);
        using var _ = PushRotatedFrame(context, box, t.Rotation, out var frame);
        var typeface = TypefaceFor(t.FontFamily, t.Bold, t.Italic);
        var brush = new SolidColorBrush(Color.FromRgb(t.Color.R, t.Color.G, t.Color.B));

        FormattedText Format(double em)
        {
            var formatted = new FormattedText(t.Text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, Math.Max(0.1, em), brush);
            if (t.WordWrap)
            {
                formatted.MaxTextWidth = Math.Max(1, frame.Width);
            }

            var decorations = new TextDecorationCollection();
            if (t.Underline)
            {
                decorations.AddRange(TextDecorations.Underline);
            }

            if (t.StrikeOut)
            {
                decorations.AddRange(TextDecorations.Strikethrough);
            }

            if (decorations.Count > 0)
            {
                formatted.SetTextDecorations(decorations);
            }

            return formatted;
        }

        FormattedText text;
        if (t.EmSize is { } em)
        {
            text = Format(em * view.Scale);
        }
        else
        {
            // Not fixed: scale the text so it fills the field (LEdit "fix font size" off).
            var probe = Format(100);
            var fit = Math.Min(frame.Width / Math.Max(1, probe.WidthIncludingTrailingWhitespace), frame.Height / Math.Max(1, probe.Height));
            text = Format(100 * fit);
        }

        // LEdit centres the text line vertically in its field, even when it is taller than the field.
        context.DrawText(text, new Point(0, (frame.Height - text.Height) / 2));
    }

    private static void PaintLinearBarcode(DrawingContext context, LinearBarcodeItem b, ViewTransform view)
    {
        var box = view.ToView(b.Bounds);
        using var _ = PushRotatedFrame(context, box, b.Rotation, out var frame);
        var module = frame.Width / b.Barcode.TotalModules;
        double x = 0;
        for (var i = 0; i < b.Barcode.Elements.Count; i++)
        {
            var width = b.Barcode.Elements[i] * module;
            if (i % 2 == 0)
            {
                context.DrawRectangle(Ink, null, new Rect(x, 0, width, frame.Height));
            }

            x += width;
        }
    }

    private static void PaintMatrix(DrawingContext context, MatrixBarcodeItem m, ViewTransform view)
    {
        var box = view.ToView(m.Bounds);
        var size = m.Modules.GetLength(0);
        var side = Math.Min(box.Width, box.Height);
        var module = side / size;
        var origin = new Point(box.X + ((box.Width - side) / 2), box.Y + ((box.Height - side) / 2));
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                if (m.Modules[y, x])
                {
                    // Slight overlap avoids hairline gaps between modules when anti-aliased.
                    context.DrawRectangle(Ink, null, new Rect(origin.X + (x * module), origin.Y + (y * module), module + 0.3, module + 0.3));
                }
            }
        }
    }

    private static void PaintError(DrawingContext context, BarcodeErrorItem e, ViewTransform view)
    {
        var box = view.ToView(e.Bounds);
        context.DrawRectangle(WarningFill, new Pen(Brushes.OrangeRed, 1), box);
        var text = new FormattedText($"⚠ {e.Message}\n{e.Data}", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 10, Brushes.DarkRed)
        {
            MaxTextWidth = Math.Max(10, box.Width - 4),
            MaxTextHeight = Math.Max(10, box.Height - 2),
        };
        context.DrawText(text, box.TopLeft + new Point(2, 1));
    }

    private static void PaintEllipse(DrawingContext context, EllipseItem e, ViewTransform view)
    {
        var box = view.ToView(e.Bounds);
        var fill = new SolidColorBrush(Color.FromRgb(e.Fill.R, e.Fill.G, e.Fill.B));
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(e.Pen.R, e.Pen.G, e.Pen.B)), Math.Max(1, e.PenWidth * 200 * view.Scale));
        context.DrawEllipse(fill, pen, box.Center, box.Width / 2, box.Height / 2);
    }

    private void PaintImage(DrawingContext context, ImageItem i, ViewTransform view)
    {
        var box = view.ToView(i.Bounds);
        if (LoadBitmap(i.Path) is { } bitmap)
        {
            context.DrawImage(bitmap, box);
        }
        else
        {
            PaintPlaceholder(context, box, $"Bitmap not found: {Path.GetFileName(i.Path)}");
        }
    }

    private void PaintCornerLogo(DrawingContext context, CornerLogoItem c, ViewTransform view)
    {
        var box = view.ToView(c.Bounds);
        var band = Math.Min(box.Width, box.Height) * Math.Clamp(c.Logo.EdgeThicknessPercent, 1, 40) / 100.0;
        var bandBrush = new SolidColorBrush(Color.FromRgb(90, 90, 90));
        context.DrawRectangle(Paper, new Pen(Ink, 1), box);
        context.DrawRectangle(bandBrush, null, new Rect(box.X, box.Y, box.Width, band));
        context.DrawRectangle(bandBrush, null, new Rect(box.X, box.Bottom - band, box.Width, band));
        context.DrawRectangle(bandBrush, null, new Rect(box.X, box.Y, band, box.Height));
        context.DrawRectangle(bandBrush, null, new Rect(box.Right - band, box.Y, band, box.Height));
        var em = Math.Max(6, Math.Min(box.Height, box.Width) / 9);
        void Label(string text, Point center)
        {
            var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, em, Ink);
            context.DrawText(formatted, new Point(center.X - (formatted.Width / 2), center.Y - (formatted.Height / 2)));
        }

        Label(c.Logo.TextTop, new Point(box.Center.X, box.Y + band + em));
        Label(c.Logo.TextBottom, new Point(box.Center.X, box.Bottom - band - em));
        Label(c.Logo.TextLeft, new Point(box.X + band + (box.Width / 5), box.Center.Y));
        Label(c.Logo.TextRight, new Point(box.Right - band - (box.Width / 5), box.Center.Y));
    }

    /// <summary>LEdit shows frames for print-time content as a box with a diagonal cross.</summary>
    private void PaintPlaceholder(DrawingContext context, Rect box, string caption)
    {
        if (!ShowDesignAids)
        {
            return;
        }

        context.DrawRectangle(null, FramePen, box);
        context.DrawLine(FramePen, box.TopLeft, box.BottomRight);
        context.DrawLine(FramePen, box.TopRight, box.BottomLeft);
        var text = new FormattedText(caption, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 11, Brushes.DimGray)
        {
            MaxTextWidth = Math.Max(10, box.Width - 4),
        };
        context.DrawText(text, box.TopLeft + new Point(3, 2));
    }

    private Bitmap? LoadBitmap(string path)
    {
        if (_bitmaps.TryGetValue(path, out var cached))
        {
            return cached;
        }

        Bitmap? bitmap = null;
        try
        {
            if (File.Exists(path))
            {
                bitmap = new Bitmap(path);
            }
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException or InvalidOperationException)
        {
            bitmap = null;
        }

        _bitmaps[path] = bitmap;
        return bitmap;
    }
}
