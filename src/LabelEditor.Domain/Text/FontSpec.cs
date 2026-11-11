namespace LabelEditor.Domain.Text;

/// <summary>Text direction. LEdit offers exactly these four (Escapement in tenths of a degree, counter-clockwise).</summary>
public enum TextRotation
{
    None = 0,
    Ccw90 = 900,
    Rotate180 = 1800,
    Cw90 = 2700,
}

/// <summary>A Win32 LOGFONT as stored in LEdit <c>&lt;Font&gt;</c>. Values are kept verbatim for fidelity.</summary>
public sealed record FontSpec
{
    public static FontSpec Default { get; } = new()
    {
        Height = -19,
        Weight = 400,
        OutPrecision = 3,
        ClipPrecision = 2,
        Quality = 1,
        PitchAndFamily = 34,
        FaceName = "Arial",
    };

    /// <summary>Font LEdit writes into new data records.</summary>
    public static FontSpec DataRecordDefault { get; } = new() { Height = -13, CharSet = 1, FaceName = "Arial" };

    public int Height { get; init; }

    public int Width { get; init; }

    /// <summary>Rotation in tenths of a degree, counter-clockwise.</summary>
    public int Escapement { get; init; }

    public int Orientation { get; init; }

    public int Weight { get; init; }

    /// <summary>Non-zero means italic (LEdit writes 255).</summary>
    public int Italic { get; init; }

    public int Underline { get; init; }

    public int StrikeOut { get; init; }

    public int CharSet { get; init; }

    public int OutPrecision { get; init; }

    public int ClipPrecision { get; init; }

    public int Quality { get; init; }

    public int PitchAndFamily { get; init; }

    public string FaceName { get; init; } = "Arial";

    public bool IsBold => Weight >= 600;

    public bool IsItalic => Italic != 0;

    public bool IsUnderline => Underline != 0;

    public bool IsStrikeOut => StrikeOut != 0;

    public TextRotation Rotation => (((Escapement % 3600) + 3600) % 3600) switch
    {
        900 => TextRotation.Ccw90,
        1800 => TextRotation.Rotate180,
        2700 => TextRotation.Cw90,
        _ => TextRotation.None,
    };

    public FontSpec WithRotation(TextRotation rotation) => this with { Escapement = (int)rotation };

    public FontSpec WithBold(bool bold) => this with { Weight = bold ? 700 : 400 };

    public FontSpec WithItalic(bool italic) => this with { Italic = italic ? 255 : 0 };

    public FontSpec WithUnderline(bool underline) => this with { Underline = underline ? 1 : 0 };

    public FontSpec WithStrikeOut(bool strikeOut) => this with { StrikeOut = strikeOut ? 1 : 0 };
}
