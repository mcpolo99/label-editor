using LabelEditor.Domain.Entries;
using LabelEditor.Domain.Geometry;
using LabelEditor.Domain.Text;
using LabelEditor.Rendering.Barcodes;

namespace LabelEditor.Rendering.Scene;

/// <summary>Everything needed to paint a label, in layout units (Y down), in z-order.</summary>
public sealed record LabelScene(int Width, int Height, MeasurementUnit Unit, IReadOnlyList<SceneItem> Items);

/// <summary>A drawable element of the scene; <see cref="Id"/> links it back to the entry for hit-testing and selection.</summary>
public abstract record SceneItem(EntryId Id, LabelRect Bounds);

/// <summary>Text drawn inside its field box.</summary>
/// <param name="Id">Entry id.</param>
/// <param name="Bounds">Field box.</param>
/// <param name="Text">Resolved text (fields filled in).</param>
/// <param name="FontFamily">Font face name.</param>
/// <param name="EmSize">Font em size in layout units; null = scale the text to fill the box.</param>
/// <param name="Bold">Bold weight.</param>
/// <param name="Italic">Italic style.</param>
/// <param name="Underline">Underlined.</param>
/// <param name="StrikeOut">Struck out.</param>
/// <param name="Rotation">Text direction.</param>
/// <param name="Color">Text colour.</param>
/// <param name="WordWrap">Wrap at the box width.</param>
/// <param name="HasPlaceholder">Design view only: a field without a sample value shows its "[FieldName]" instead, so the text is not what prints.</param>
public sealed record TextItem(
    EntryId Id,
    LabelRect Bounds,
    string Text,
    string FontFamily,
    double? EmSize,
    bool Bold,
    bool Italic,
    bool Underline,
    bool StrikeOut,
    TextRotation Rotation,
    RgbColor Color,
    bool WordWrap,
    bool HasPlaceholder = false) : SceneItem(Id, Bounds);

/// <summary>An encoded 1-D bar code; bars run across the (rotated) box.</summary>
public sealed record LinearBarcodeItem(EntryId Id, LabelRect Bounds, LinearBarcode Barcode, string Data, TextRotation Rotation) : SceneItem(Id, Bounds);

/// <summary>A 2-D (QR) code, drawn as a square centred in the box.</summary>
public sealed record MatrixBarcodeItem(EntryId Id, LabelRect Bounds, bool[,] Modules, string Data) : SceneItem(Id, Bounds);

/// <summary>A bar code that cannot be encoded (shown as a warning box).</summary>
public sealed record BarcodeErrorItem(EntryId Id, LabelRect Bounds, string Data, string Message) : SceneItem(Id, Bounds);

public sealed record EllipseItem(EntryId Id, LabelRect Bounds, int PenWidth, RgbColor Pen, RgbColor Fill) : SceneItem(Id, Bounds);

/// <summary>Line from Bounds.Start to Bounds.End.</summary>
public sealed record LineItem(EntryId Id, LabelRect Bounds) : SceneItem(Id, Bounds);

/// <summary>Bitmap file; the UI shows a crossed frame when the file is missing.</summary>
public sealed record ImageItem(EntryId Id, LabelRect Bounds, string Path) : SceneItem(Id, Bounds);

/// <summary>Content supplied at print time (part graphics); drawn as a crossed frame with a caption.</summary>
public sealed record PlaceholderItem(EntryId Id, LabelRect Bounds, string Caption) : SceneItem(Id, Bounds);

/// <summary>Corner/edge logo: four edge bands with their texts.</summary>
/// <summary>
/// The edge indicator: a part outline with banding on the sides that have an edge.
/// </summary>
/// <param name="Id">The entry.</param>
/// <param name="Bounds">The entry's box.</param>
/// <param name="Logo">The entry's settings.</param>
/// <param name="Top">Text of the top side (A top); empty = no edge on that side.</param>
/// <param name="Bottom">Bottom side (A bottom).</param>
/// <param name="Left">Left side (B left).</param>
/// <param name="Right">Right side (B right).</param>
/// <param name="PartAspect">Length / width of the previewed part when known; the outline keeps it if the logo maintains the aspect ratio.</param>
public sealed record CornerLogoItem(
    EntryId Id,
    LabelRect Bounds,
    CornerLogoEntry Logo,
    string Top,
    string Bottom,
    string Left,
    string Right,
    double? PartAspect = null) : SceneItem(Id, Bounds)
{
    public CornerLogoItem(EntryId id, LabelRect bounds, CornerLogoEntry logo)
        : this(id, bounds, logo, logo.TextTop, logo.TextBottom, logo.TextLeft, logo.TextRight)
    {
    }
}
