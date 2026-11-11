namespace LabelEditor.Domain.Entries;

/// <summary>An RGB colour as stored by LEdit ("r,g,b").</summary>
public readonly record struct RgbColor(byte R, byte G, byte B)
{
    public static RgbColor Black => new(0, 0, 0);

    public static RgbColor White => new(255, 255, 255);

    /// <summary>Win32 COLORREF (0x00BBGGRR), used by <c>FontOptions/@TextColor</c>.</summary>
    public static RgbColor FromColorRef(int colorRef) =>
        new((byte)(colorRef & 0xFF), (byte)((colorRef >> 8) & 0xFF), (byte)((colorRef >> 16) & 0xFF));

    public int ToColorRef() => R | (G << 8) | (B << 16);

    public override string ToString() => $"{R},{G},{B}";
}

/// <summary>Ellipse / circle. LEdit type 6.</summary>
public sealed record EllipseEntry : LabelEntry
{
    public override EntryKind Kind => EntryKind.Ellipse;

    public int PenWidth { get; init; } = 1;

    /// <summary>Win32 pen style (0 = solid).</summary>
    public int PenStyle { get; init; }

    public RgbColor PenColor { get; init; } = RgbColor.Black;

    public RgbColor BrushColor { get; init; } = RgbColor.White;
}

/// <summary>Straight line from Bounds.Start to Bounds.End. LEdit type 5.</summary>
public sealed record LineEntry : LabelEntry
{
    public override EntryKind Kind => EntryKind.Line;

    public override bool IsSegment => true;
}

/// <summary>A fixed bitmap file drawn into the field. LEdit type 3.</summary>
public sealed record BitmapEntry : LabelEntry
{
    public override EntryKind Kind => EntryKind.Bitmap;

    /// <summary>Path of the bitmap as stored in the file (typically absolute, machine specific).</summary>
    public string Path { get; init; } = string.Empty;
}

/// <summary>A part-specific graphic supplied by the optimized parts list ("Draw 1..4"). LEdit type 4.</summary>
public sealed record LabelGraphicEntry : LabelEntry
{
    public override EntryKind Kind => EntryKind.LabelGraphic;

    /// <summary>Zero-based graphic slot (0 = Draw 1).</summary>
    public int GraphicIndex { get; init; }
}

/// <summary>
/// Edge indicator ("corner logo"): the part drawn as a rectangle with its edge banding. LEdit type 8.
/// <para>
/// It has four inputs, one per side of the part (top = A top, bottom = A bottom, left = B left, right = B right).
/// When printing, each side shows the part's edge data for that side (material, colour or type, see
/// <see cref="EdgeInfo"/>); a side without an edge has no banding. The four texts here are the design-time samples;
/// an empty text means "no edge on this side" (LEdit then leaves the attribute out).
/// </para>
/// </summary>
public sealed record CornerLogoEntry : LabelEntry
{
    public override EntryKind Kind => EntryKind.CornerLogo;

    /// <summary>Maintain aspect ratio (CornerLogoData/@ImageProp).</summary>
    public bool KeepAspectRatio { get; init; } = true;

    /// <summary>Print edge material (CornerLogoData/@PrnMatEdge).</summary>
    public bool PrintEdgeMaterial { get; init; } = true;

    /// <summary>Edge information printing mode (CornerLogoData/@EdgeInfo): 0 material, 1 colour, 2 type (order of the LEdit dialog).</summary>
    public int EdgeInfo { get; init; }

    /// <summary>
    /// CornerLogoData/@Orient, written by newer LEdit versions (null when the file has none). Its meaning is not
    /// documented; the value is kept and can be edited.
    /// </summary>
    public int? Orientation { get; init; }

    public string TextTop { get; init; } = "Top_Edge";

    public string TextBottom { get; init; } = "Bottom_Edge";

    public string TextLeft { get; init; } = "Left_Edge";

    public string TextRight { get; init; } = "Right_Edge";

    /// <summary>Part data field per side for the chosen <see cref="EdgeInfo"/>, e.g. MatEdgeUp, ColEdgeL, TypeEdgeR.</summary>
    public (string Top, string Bottom, string Left, string Right) EdgeFieldNames
    {
        get
        {
            var prefix = EdgeInfo switch { 1 => "Col", 2 => "Type", _ => "Mat" };
            return ($"{prefix}EdgeUp", $"{prefix}EdgeLo", $"{prefix}EdgeL", $"{prefix}EdgeR");
        }
    }

    /// <summary>Edge thickness in % used for display.</summary>
    public int EdgeThicknessPercent { get; init; } = 8;
}

/// <summary>
/// The label's data-record definition (font used for file printing). Present in every LEdit label,
/// not drawn on the canvas. LEdit type 9.
/// </summary>
public sealed record DataRecordEntry : LabelEntry
{
    public override EntryKind Kind => EntryKind.DataRecord;

    public override bool IsVisual => false;

    public Text.FontSpec Font { get; init; } = Text.FontSpec.DataRecordDefault;
}

/// <summary>
/// An entry the editor does not understand. Its original content is carried verbatim so that
/// saving a file never silently drops data written by a newer LEdit.
/// </summary>
public sealed record UnknownEntry : LabelEntry
{
    public override EntryKind Kind => EntryKind.Unknown;

    public required string ElementName { get; init; }

    public int TypeCode { get; init; }

    /// <summary>Opaque serialized form owned by the file format that produced it.</summary>
    public required string OpaqueContent { get; init; }
}
