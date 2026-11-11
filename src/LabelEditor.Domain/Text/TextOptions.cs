namespace LabelEditor.Domain.Text;

/// <summary>
/// Bar code switches packed in <c>FontOptions/@BarcodeSettings</c>.
/// <para>
/// Decoded from the LEdit dialog and sampled files: bit 16 marks the text as a bar code and the low
/// bits are the dialog check boxes in order (start, stop, compress, check). The symbology itself is
/// determined by the bar code font (e.g. C28N = Code 128, A39N = Code 39). The raw value is kept so
/// unknown bits survive a round trip.
/// </para>
/// </summary>
public readonly record struct BarcodeSettings(int Raw)
{
    private const int EnabledBit = 1 << 16;
    private const int StartBit = 1 << 0;
    private const int StopBit = 1 << 1;
    private const int CompressBit = 1 << 2;
    private const int CheckBit = 1 << 3;

    public static BarcodeSettings None => default;

    /// <summary>Default for a new bar code: start + stop characters (what LEdit files most often use).</summary>
    public static BarcodeSettings DefaultBarcode => new(EnabledBit | StartBit | StopBit);

    public bool IsBarcode => (Raw & EnabledBit) != 0;

    public bool AddStartCharacter => (Raw & StartBit) != 0;

    public bool AddStopCharacter => (Raw & StopBit) != 0;

    public bool Compress => (Raw & CompressBit) != 0;

    public bool AddCheckCharacter => (Raw & CheckBit) != 0;

    public BarcodeSettings WithBarcode(bool value) => Set(EnabledBit, value);

    public BarcodeSettings WithStartCharacter(bool value) => Set(StartBit, value);

    public BarcodeSettings WithStopCharacter(bool value) => Set(StopBit, value);

    public BarcodeSettings WithCompress(bool value) => Set(CompressBit, value);

    public BarcodeSettings WithCheckCharacter(bool value) => Set(CheckBit, value);

    private BarcodeSettings Set(int bit, bool value) => new(value ? Raw | bit : Raw & ~bit);
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
