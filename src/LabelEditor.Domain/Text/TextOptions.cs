namespace LabelEditor.Domain.Text;

/// <summary>
/// Bar code symbologies, as LEdit codes them in the low word of <c>FontOptions/@BarcodeSettings</c>.
/// <para>
/// Values are inferred from real labels compared with LEdit's own previews: 1 and 3 always use the
/// C28N font (Code 128 family), 2 the A39N font (Code 39), and 12 renders as a QR code. LEdit also
/// offers PDF417, DataMatrix, EAN-8/13 and 2-of-5 variants whose codes are not yet known; such values
/// are preserved and drawn as a marked placeholder.
/// </para>
/// </summary>
public enum BarcodeType
{
    Code128 = 1,
    Code39 = 2,
    Code128C = 3,
    QrCode = 12,
}

/// <summary>
/// <c>FontOptions/@BarcodeSettings</c>: 0x10000 marks the text as a bar code, the low word is the
/// <see cref="BarcodeType"/> code. The raw value is kept so unknown codes survive a round trip.
/// </summary>
public readonly record struct BarcodeSettings(int Raw)
{
    private const int EnabledBit = 1 << 16;

    public static BarcodeSettings None => default;

    /// <summary>What most existing labels use for new bar codes.</summary>
    public static BarcodeSettings DefaultBarcode => For(BarcodeType.Code128C);

    public static BarcodeSettings For(BarcodeType type) => new(EnabledBit | (int)type);

    public bool IsBarcode => (Raw & EnabledBit) != 0;

    /// <summary>The symbology code (low word), whether or not it is a known <see cref="BarcodeType"/>.</summary>
    public int SymbologyCode => Raw & 0xFFFF;

    /// <summary>The symbology, or null when the code is not one the editor knows.</summary>
    public BarcodeType? Type => Enum.IsDefined(typeof(BarcodeType), SymbologyCode) ? (BarcodeType)SymbologyCode : null;

    public BarcodeSettings WithBarcode(bool value) => new(value ? Raw | EnabledBit : Raw & ~EnabledBit);

    public BarcodeSettings WithType(BarcodeType type) => new((Raw & ~0xFFFF) | (int)type);
}

/// <summary>LEdit <c>&lt;FontOptions&gt;</c>. Optional attributes are null when the file did not contain them.</summary>
public sealed record TextOptions
{
    /// <summary>When true the text uses <see cref="FontSize"/>; otherwise it is scaled to fill the field.</summary>
    public bool FontSizeFixed { get; init; } = true;

    /// <summary>Font size in tenths of a point (100 = 10 pt).</summary>
    public int FontSize { get; init; } = 100;

    public bool UpperCase { get; init; }

    public BarcodeSettings Barcode { get; init; }

    /// <summary>Win32 COLORREF; null when the attribute is absent (older files).</summary>
    public int? TextColor { get; init; } = 0;

    public int? CodePage { get; init; } = 0;

    /// <summary>"Word wrap"; null when absent (files older than the option).</summary>
    public bool? Multiline { get; init; } = false;

    public double FontSizePoints => FontSize / 10.0;
}

/// <summary>
/// Filler character mode of a text element (<c>FillChar</c>). Values other than these are kept verbatim
/// in <see cref="TextElement.FillCharacter"/>; the mapping of 1..3 follows the LEdit dialog order and is
/// not yet verified against a file written with those options.
/// </summary>
public enum FillCharacterMode
{
    NotSet = -1,
    None = 0,
    LeadingZeros = 1,
    LeadingBlanks = 2,
    TrailingBlanks = 3,
}
