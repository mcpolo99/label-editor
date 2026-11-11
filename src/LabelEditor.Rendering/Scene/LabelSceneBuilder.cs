using LabelEditor.Application.Preview;
using LabelEditor.Domain.Entries;
using LabelEditor.Domain.Geometry;
using LabelEditor.Domain.Layout;
using LabelEditor.Rendering.Barcodes;

namespace LabelEditor.Rendering.Scene;

/// <summary>Builds the scene for a layout, filling fields from a data record or design-time previews.</summary>
public sealed class LabelSceneBuilder(FieldTextResolver resolver)
{
    /// <summary>Layout units per typographic point (1/72 inch).</summary>
    public static double LayoutUnitsPerPoint(MeasurementUnit unit) =>
        unit == MeasurementUnit.Inch ? LayoutUnits.PerUnit / 72.0 : LayoutUnits.PerUnit * LayoutUnits.MillimetresPerInch / 72.0;

    public LabelScene Build(LabelLayout layout, PreviewRecord? record = null)
    {
        var unit = layout.Header.Unit;
        var items = layout.VisualEntries.Select(e => BuildItem(e, unit, record)).OfType<SceneItem>().ToList();
        return new LabelScene(layout.Header.Shape.Width, layout.Header.Shape.Height, unit, items);
    }

    private SceneItem? BuildItem(LabelEntry entry, MeasurementUnit unit, PreviewRecord? record) => entry switch
    {
        TextEntry t when t.IsBarcode || BarcodeFonts.IsBarcodeFont(t.Font.FaceName) => BuildBarcode(t, record),
        TextEntry t => new TextItem(
            t.Id,
            t.Bounds.Normalize(),
            resolver.PreviewText(t, record),
            t.Font.FaceName,
            t.Options.FontSizeFixed ? t.Options.FontSizePoints * LayoutUnitsPerPoint(unit) : null,
            t.Font.IsBold,
            t.Font.IsItalic,
            t.Font.IsUnderline,
            t.Font.IsStrikeOut,
            t.Font.Rotation,
            t.Options.TextColor is { } c ? RgbColor.FromColorRef(c) : RgbColor.Black,
            t.Options.Multiline ?? false),
        EllipseEntry e => new EllipseItem(e.Id, e.Bounds.Normalize(), e.PenWidth, e.PenColor, e.BrushColor),
        LineEntry l => new LineItem(l.Id, l.Bounds),
        BitmapEntry b => new ImageItem(b.Id, b.Bounds.Normalize(), b.Path),
        LabelGraphicEntry g => new PlaceholderItem(g.Id, g.Bounds.Normalize(), $"Draw {g.GraphicIndex + 1}"),
        CornerLogoEntry c => new CornerLogoItem(c.Id, c.Bounds.Normalize(), c),
        UnknownEntry u => new PlaceholderItem(u.Id, u.Bounds.Normalize(), $"<{u.ElementName}>"),
        _ => null,
    };

    private SceneItem BuildBarcode(TextEntry entry, PreviewRecord? record)
    {
        var data = resolver.BarcodeData(entry, record);
        var bounds = entry.Bounds.Normalize();
        var symbology = BarcodeFonts.SymbologyFor(entry.Font.FaceName);
        var result = BarcodeEncoder.Encode(symbology, data, entry.Options.Barcode.AddCheckCharacter);
        return result switch
        {
            { Linear: { } linear } => new LinearBarcodeItem(entry.Id, bounds, linear, data, entry.Font.Rotation),
            { Matrix: { } matrix } => new MatrixBarcodeItem(entry.Id, bounds, matrix, data),
            _ => new BarcodeErrorItem(entry.Id, bounds, data, result.Error ?? "Cannot encode"),
        };
    }
}
