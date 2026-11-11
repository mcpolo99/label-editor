using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using LabelEditor.App.Rendering;
using LabelEditor.Domain.Geometry;
using LabelEditor.Presentation.ViewModels;

namespace LabelEditor.App.Controls;

/// <summary>
/// A ruler along the top or left of the canvas in the label's unit (mm or inch), zero at the label's corner.
/// It follows the canvas scroll position and marks the label, the selection and the pointer.
/// </summary>
public sealed class Ruler : Control
{
    public const double Thickness = 20;

    public static readonly StyledProperty<DocumentViewModel?> DocumentProperty =
        AvaloniaProperty.Register<Ruler, DocumentViewModel?>(nameof(Document));

    public static readonly StyledProperty<Orientation> OrientationProperty =
        AvaloniaProperty.Register<Ruler, Orientation>(nameof(Orientation));

    /// <summary>Scroll offset of the canvas (the ruler uses X or Y according to its orientation).</summary>
    public static readonly StyledProperty<Vector> OffsetProperty =
        AvaloniaProperty.Register<Ruler, Vector>(nameof(Offset));

    private static readonly IBrush LightBackground = new SolidColorBrush(Color.FromRgb(240, 241, 243));
    private static readonly IBrush DarkBackground = new SolidColorBrush(Color.FromRgb(43, 45, 48));
    private static readonly IBrush LightLabel = Brushes.White;
    private static readonly IBrush DarkLabel = new SolidColorBrush(Color.FromRgb(60, 63, 67));
    private static readonly IBrush LightInk = new SolidColorBrush(Color.FromRgb(90, 90, 90));
    private static readonly IBrush DarkInk = new SolidColorBrush(Color.FromRgb(175, 178, 184));
    private static readonly IBrush SelectionBand = new SolidColorBrush(Color.FromArgb(70, 0, 120, 215));
    private static readonly IPen PointerPen = new Pen(new SolidColorBrush(Color.FromRgb(230, 0, 160)), 1);

    private DocumentViewModel? _subscribed;

    static Ruler()
    {
        AffectsRender<Ruler>(DocumentProperty, OrientationProperty, OffsetProperty);
    }

    public DocumentViewModel? Document
    {
        get => GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    public Orientation Orientation
    {
        get => GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    public Vector Offset
    {
        get => GetValue(OffsetProperty);
        set => SetValue(OffsetProperty, value);
    }

    private bool IsHorizontal => Orientation == Orientation.Horizontal;

    protected override Size MeasureOverride(Size availableSize) => IsHorizontal ? new Size(0, Thickness) : new Size(Thickness, 0);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DocumentProperty)
        {
            Subscribe(change.NewValue as DocumentViewModel);
        }
        else if (change.Property == ThemeVariantScope.ActualThemeVariantProperty)
        {
            InvalidateVisual();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Subscribe(Document);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Subscribe(null);
        base.OnDetachedFromVisualTree(e);
    }

    private void Subscribe(DocumentViewModel? document)
    {
        if (_subscribed is not null)
        {
            _subscribed.RenderInvalidated -= OnInvalidated;
            _subscribed.PropertyChanged -= OnDocumentPropertyChanged;
        }

        _subscribed = document;
        if (document is not null)
        {
            document.RenderInvalidated += OnInvalidated;
            document.PropertyChanged += OnDocumentPropertyChanged;
        }

        InvalidateVisual();
    }

    private void OnInvalidated(object? sender, EventArgs e) => InvalidateVisual();

    private void OnDocumentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DocumentViewModel.PointerPosition) or nameof(DocumentViewModel.Zoom))
        {
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        var dark = ActualThemeVariant == ThemeVariant.Dark;
        var size = Bounds.Size;
        context.FillRectangle(dark ? DarkBackground : LightBackground, new Rect(size));
        if (Document is not { } document)
        {
            return;
        }

        var header = document.Layout.Header;
        var scale = ViewTransform.ScaleFor(document.Zoom, header.Unit);
        var origin = LabelCanvas.CanvasMargin - (IsHorizontal ? Offset.X : Offset.Y);
        var length = IsHorizontal ? header.Shape.Width : header.Shape.Height;
        var extent = IsHorizontal ? size.Width : size.Height;
        double At(double units) => origin + (units * scale);

        // The label's span, then the selection's.
        Band(context, At(0), At(length), dark ? DarkLabel : LightLabel);
        var selected = document.Session.Selection.Ids.Select(document.Layout.Find).OfType<Domain.Entries.LabelEntry>().Select(e => e.Bounds.Normalize()).ToList();
        if (selected.Count > 0)
        {
            var union = selected.Aggregate((a, b) => a.Union(b));
            Band(context, At(IsHorizontal ? union.Left : union.Top), At(IsHorizontal ? union.Right : union.Bottom), SelectionBand);
        }

        DrawTicks(context, header.Unit, scale, origin, extent, dark ? DarkInk : LightInk);

        if (document.PointerPosition is { } pointer)
        {
            var p = Math.Round(At(IsHorizontal ? pointer.X : pointer.Y)) + 0.5;
            context.DrawLine(PointerPen, Point(p, 0), Point(p, Thickness));
        }

        var edge = new Pen(dark ? DarkInk : LightInk, 1);
        context.DrawLine(edge, IsHorizontal ? new Point(0, size.Height - 0.5) : new Point(size.Width - 0.5, 0), IsHorizontal ? new Point(size.Width, size.Height - 0.5) : new Point(size.Width - 0.5, size.Height));
    }

    /// <summary>Minor ticks with longer ones at the halves and numbered major ticks, spaced so they never crowd.</summary>
    private void DrawTicks(DrawingContext context, MeasurementUnit unit, double scale, double origin, double extent, IBrush ink)
    {
        // Steps in label units (mm or inch); minor tick at least 4 DIPs apart, numbers at least 40 apart.
        double[] minors = unit == MeasurementUnit.Inch ? [0.0625, 0.125, 0.25, 0.5, 1, 2, 5, 10] : [0.5, 1, 2, 5, 10, 20, 50, 100];
        double[] majors = unit == MeasurementUnit.Inch ? [0.5, 1, 2, 5, 10, 20, 50] : [5, 10, 20, 50, 100, 200, 500];
        var perUnit = scale * LayoutUnits.PerUnit; // DIPs per mm / inch
        var minor = minors.FirstOrDefault(s => s * perUnit >= 4, minors[^1]);
        var major = majors.FirstOrDefault(s => s * perUnit >= 40 && s >= minor, majors[^1]);
        var pen = new Pen(ink, 1);

        var first = Math.Floor(-origin / perUnit / minor) * minor;
        var last = (extent - origin) / perUnit;
        for (var value = first; value <= last; value += minor)
        {
            var p = Math.Round(origin + (value * perUnit)) + 0.5;
            var isMajor = IsMultiple(value, major);
            var isHalf = !isMajor && IsMultiple(value, major / 2);
            var tick = isMajor ? Thickness * 0.6 : isHalf ? Thickness * 0.4 : Thickness * 0.22;
            context.DrawLine(pen, Point(p, Thickness - tick), Point(p, Thickness));
            if (isMajor)
            {
                var text = new FormattedText(value.ToString("0.##", CultureInfo.CurrentCulture), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 9, ink);
                if (IsHorizontal)
                {
                    context.DrawText(text, new Point(p + 2, 0));
                }
                else
                {
                    // Read from the bottom up, next to the tick.
                    using var _ = context.PushTransform(Matrix.CreateRotation(-Math.PI / 2) * Matrix.CreateTranslation(0, p - 2));
                    context.DrawText(text, new Point(0, 0));
                }
            }
        }
    }

    private static bool IsMultiple(double value, double step) => Math.Abs((value / step) - Math.Round(value / step)) < 1e-6;

    private void Band(DrawingContext context, double from, double to, IBrush brush) =>
        context.FillRectangle(brush, IsHorizontal ? new Rect(from, 0, Math.Max(0, to - from), Thickness) : new Rect(0, from, Thickness, Math.Max(0, to - from)));

    /// <summary>A point given along the ruler and across it.</summary>
    private Point Point(double along, double across) => IsHorizontal ? new Point(along, across) : new Point(across, along);
}
