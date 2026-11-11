using LabelEditor.Domain.Entries;
using LabelEditor.Domain.Geometry;
using LabelEditor.Domain.Text;

namespace LabelEditor.Application.Editing;

/// <summary>Drawing tools offered by the editor (LEdit toolbar).</summary>
public enum EditorTool
{
    Select,
    Text,
    Barcode,
    QrCode,
    Line,
    Ellipse,
    Bitmap,
    LabelGraphic,
    CornerLogo,
}

/// <summary>Creates new entries with the same defaults LEdit uses.</summary>
public static class EntryFactory
{
    /// <summary>Smallest size of a newly drawn box, in layout units (1 mm).</summary>
    public const int MinimumSize = 1000;

    public static bool CreatesEntry(EditorTool tool) => tool != EditorTool.Select;

    /// <summary>Default size used when a tool is clicked without dragging.</summary>
    public static (int Width, int Height) DefaultSize(EditorTool tool) => tool switch
    {
        EditorTool.Text => (30_000, 6_000),
        EditorTool.Barcode => (50_000, 10_000),
        EditorTool.QrCode => (15_000, 15_000),
        EditorTool.Line => (30_000, 0),
        EditorTool.Ellipse => (6_000, 6_000),
        EditorTool.CornerLogo => (30_000, 20_000),
        _ => (20_000, 15_000),
    };

    /// <param name="tool">Tool that draws the entry.</param>
    /// <param name="bounds">Field rectangle; for lines the signed start→end segment.</param>
    /// <param name="fieldId">Data field for text tools; null creates fixed text.</param>
    /// <param name="previewText">Optional sample value for the field.</param>
    public static LabelEntry Create(EditorTool tool, LabelRect bounds, int? fieldId = null, string? previewText = null)
    {
        var id = EntryId.New();
        var box = bounds.Normalize();
        return tool switch
        {
            EditorTool.Text => new TextEntry
            {
                Id = id,
                Bounds = box,
                Elements = [fieldId is { } f ? TextElement.ForField(f, previewText) : TextElement.ForLiteral("Text")],
            },
            EditorTool.Barcode => new TextEntry
            {
                Id = id,
                Bounds = box,
                Font = FontSpec.Default with { FaceName = "C28N", Height = -37, PitchAndFamily = 2 },
                Options = new TextOptions { FontSize = 280, Barcode = BarcodeSettings.DefaultBarcode },
                Elements = [fieldId is { } f ? TextElement.ForField(f, previewText) : TextElement.ForLiteral("123456")],
            },
            EditorTool.QrCode => new TextEntry
            {
                Id = id,
                Bounds = box,
                Options = new TextOptions { Barcode = BarcodeSettings.For(BarcodeType.QrCode) },
                Elements = [fieldId is { } f ? TextElement.ForField(f, previewText) : TextElement.ForLiteral("123456789")],
            },
            EditorTool.Line => new LineEntry { Id = id, Bounds = bounds },
            EditorTool.Ellipse => new EllipseEntry { Id = id, Bounds = box },
            EditorTool.Bitmap => new BitmapEntry { Id = id, Bounds = box },
            EditorTool.LabelGraphic => new LabelGraphicEntry { Id = id, Bounds = box },
            EditorTool.CornerLogo => new CornerLogoEntry { Id = id, Bounds = box },
            _ => throw new ArgumentOutOfRangeException(nameof(tool), tool, "This tool does not create entries."),
        };
    }

    /// <summary>A text entry printing one data field, sized for a typical label line.</summary>
    public static TextEntry CreateFieldText(int fieldId, LabelPoint position, string? previewText = null) =>
        (TextEntry)Create(EditorTool.Text, new LabelRect(position.X, position.Y, 30_000, 5_000), fieldId, previewText);
}
