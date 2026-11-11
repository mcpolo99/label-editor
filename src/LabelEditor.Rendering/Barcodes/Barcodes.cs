using System.Globalization;
using QRCoder;

namespace LabelEditor.Rendering.Barcodes;

/// <summary>Bar code symbologies the editor can draw.</summary>
public enum BarcodeSymbology
{
    Code128,
    Code39,
    QrCode,
}

/// <summary>A 1-D bar code as module widths, alternating bar/space and starting with a bar.</summary>
public sealed record LinearBarcode(BarcodeSymbology Symbology, IReadOnlyList<int> Elements)
{
    public int TotalModules => Elements.Sum();
}

/// <summary>Result of encoding: a symbol or the reason the data cannot be encoded.</summary>
public sealed record BarcodeResult(LinearBarcode? Linear, bool[,]? Matrix, string? Error)
{
    public static BarcodeResult Fail(string error) => new(null, null, error);
}

/// <summary>
/// Picks the symbology for a text entry. The <c>BarcodeSettings</c> code decides; for text that only uses a
/// bar code font (C28N = Code 128, A39N = Code 39) without the bar code flag, the font decides.
/// </summary>
public static class BarcodeFonts
{
    public const string Code128Font = "C28N";
    public const string Code39Font = "A39N";

    public static bool IsBarcodeFont(string faceName) =>
        string.Equals(faceName.Trim(), Code128Font, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(faceName.Trim(), Code39Font, StringComparison.OrdinalIgnoreCase);

    /// <summary>The font LEdit uses for a symbology, or null when it draws without a bar code font (QR).</summary>
    public static string? FontFor(Domain.Text.BarcodeType type) => type switch
    {
        Domain.Text.BarcodeType.Code39 => Code39Font,
        Domain.Text.BarcodeType.QrCode => null,
        _ => Code128Font,
    };

    /// <returns>The symbology to draw, or null when the code is not supported yet.</returns>
    public static BarcodeSymbology? SymbologyFor(Domain.Text.BarcodeSettings settings, string faceName)
    {
        if (settings.IsBarcode)
        {
            return settings.Type switch
            {
                Domain.Text.BarcodeType.Code128 or Domain.Text.BarcodeType.Code128C => BarcodeSymbology.Code128,
                Domain.Text.BarcodeType.Code39 => BarcodeSymbology.Code39,
                Domain.Text.BarcodeType.QrCode => BarcodeSymbology.QrCode,
                _ => null,
            };
        }

        return faceName.Contains("39", StringComparison.Ordinal) ? BarcodeSymbology.Code39 : BarcodeSymbology.Code128;
    }
}

/// <summary>Encodes text for the given symbology.</summary>
public static class BarcodeEncoder
{
    public static BarcodeResult Encode(BarcodeSymbology symbology, string text, bool addCheckCharacter = false)
    {
        if (text.Length == 0)
        {
            return BarcodeResult.Fail("No data");
        }

        return symbology switch
        {
            BarcodeSymbology.Code128 => Code128.Encode(text) is { } c128 ? new(c128, null, null) : BarcodeResult.Fail("Code 128 supports ASCII 32–127 only"),
            BarcodeSymbology.Code39 => Code39.Encode(text, addCheckCharacter) is { } c39 ? new(c39, null, null) : BarcodeResult.Fail("Code 39 supports 0-9, A-Z, space and - . $ / + %"),
            BarcodeSymbology.QrCode => new(null, Qr.Encode(text), null),
            _ => BarcodeResult.Fail("Unsupported symbology"),
        };
    }
}

/// <summary>Code 128 with automatic switching between code sets B and C.</summary>
public static class Code128
{
    public const int StartB = 104;
    public const int StartC = 105;
    public const int SwitchToB = 100;
    public const int SwitchToC = 99;
    public const int Stop = 106;

    /// <summary>Element widths for symbol values 0..106 (106 = stop, 7 elements).</summary>
    public static IReadOnlyList<string> Patterns { get; } =
    [
        "212222", "222122", "222221", "121223", "121322", "131222", "122213", "122312", "132212", "221213",
        "221312", "231212", "112232", "122132", "122231", "113222", "123122", "123221", "223211", "221132",
        "221231", "213212", "223112", "312131", "311222", "321122", "321221", "312212", "322112", "322211",
        "212123", "212321", "232121", "111323", "131123", "131321", "112313", "132113", "132311", "211313",
        "231113", "231311", "112133", "112331", "132131", "113123", "113321", "133121", "313121", "211331",
        "231131", "213113", "213311", "213131", "311123", "311321", "331121", "312113", "312311", "332111",
        "314111", "221411", "431111", "111224", "111422", "121124", "121421", "141122", "141221", "112214",
        "112412", "122114", "122411", "142112", "142211", "241211", "221114", "413111", "241112", "134111",
        "111242", "121142", "121241", "114212", "124112", "124211", "411212", "421112", "421211", "212141",
        "214121", "412121", "111143", "111341", "131141", "114113", "114311", "411113", "411311", "113141",
        "114131", "311141", "411131", "211412", "211214", "211232", "2331112",
    ];

    /// <summary>Symbol values including start, checksum and stop; null if the text has unsupported characters.</summary>
    public static IReadOnlyList<int>? EncodeValues(string text)
    {
        if (text.Any(c => c < 32 || c > 127))
        {
            return null;
        }

        var values = new List<int>();
        var codeC = DigitRun(text, 0) >= 4 || (DigitRun(text, 0) == text.Length && text.Length % 2 == 0);
        values.Add(codeC ? StartC : StartB);
        var i = 0;
        while (i < text.Length)
        {
            if (codeC)
            {
                if (DigitRun(text, i) >= 2)
                {
                    values.Add(int.Parse(text.AsSpan(i, 2), CultureInfo.InvariantCulture));
                    i += 2;
                    continue;
                }

                values.Add(SwitchToB);
                codeC = false;
                continue;
            }

            var run = DigitRun(text, i);
            if (run >= 6 || (run >= 4 && i + run == text.Length))
            {
                if (run % 2 == 1)
                {
                    values.Add(text[i] - 32);
                    i++;
                }

                values.Add(SwitchToC);
                codeC = true;
                continue;
            }

            values.Add(text[i] - 32);
            i++;
        }

        var checksum = values[0];
        for (var position = 1; position < values.Count; position++)
        {
            checksum += values[position] * position;
        }

        values.Add(checksum % 103);
        values.Add(Stop);
        return values;
    }

    public static LinearBarcode? Encode(string text)
    {
        if (EncodeValues(text) is not { } values)
        {
            return null;
        }

        var elements = values.SelectMany(v => Patterns[v].Select(c => c - '0')).ToList();
        return new LinearBarcode(BarcodeSymbology.Code128, elements);
    }

    private static int DigitRun(string text, int start)
    {
        var i = start;
        while (i < text.Length && char.IsAsciiDigit(text[i]))
        {
            i++;
        }

        return i - start;
    }
}

/// <summary>Code 39 (3:1 wide/narrow ratio), optional mod-43 check character.</summary>
public static class Code39
{
    public const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ-. $/+%";

    /// <summary>n = narrow, w = wide; 5 bars and 4 spaces per character, '*' is start/stop.</summary>
    public static IReadOnlyDictionary<char, string> Patterns { get; } = new Dictionary<char, string>
    {
        ['0'] = "nnnwwnwnn", ['1'] = "wnnwnnnnw", ['2'] = "nnwwnnnnw", ['3'] = "wnwwnnnnn", ['4'] = "nnnwwnnnw",
        ['5'] = "wnnwwnnnn", ['6'] = "nnwwwnnnn", ['7'] = "nnnwnnwnw", ['8'] = "wnnwnnwnn", ['9'] = "nnwwnnwnn",
        ['A'] = "wnnnnwnnw", ['B'] = "nnwnnwnnw", ['C'] = "wnwnnwnnn", ['D'] = "nnnnwwnnw", ['E'] = "wnnnwwnnn",
        ['F'] = "nnwnwwnnn", ['G'] = "nnnnnwwnw", ['H'] = "wnnnnwwnn", ['I'] = "nnwnnwwnn", ['J'] = "nnnnwwwnn",
        ['K'] = "wnnnnnnww", ['L'] = "nnwnnnnww", ['M'] = "wnwnnnnwn", ['N'] = "nnnnwnnww", ['O'] = "wnnnwnnwn",
        ['P'] = "nnwnwnnwn", ['Q'] = "nnnnnnwww", ['R'] = "wnnnnnwwn", ['S'] = "nnwnnnwwn", ['T'] = "nnnnwnwwn",
        ['U'] = "wwnnnnnnw", ['V'] = "nwwnnnnnw", ['W'] = "wwwnnnnnn", ['X'] = "nwnnwnnnw", ['Y'] = "wwnnwnnnn",
        ['Z'] = "nwwnwnnnn", ['-'] = "nwnnnnwnw", ['.'] = "wwnnnnwnn", [' '] = "nwwnnnwnn", ['$'] = "nwnwnwnnn",
        ['/'] = "nwnwnnnwn", ['+'] = "nwnnnwnwn", ['%'] = "nnnwnwnwn", ['*'] = "nwnnwnwnn",
    };

    public static LinearBarcode? Encode(string text, bool addCheckCharacter)
    {
        var data = text.ToUpperInvariant();
        if (data.Any(c => !Patterns.ContainsKey(c) || c == '*'))
        {
            return null;
        }

        if (addCheckCharacter)
        {
            data += Alphabet[data.Sum(c => Alphabet.IndexOf(c)) % 43];
        }

        var elements = new List<int>();
        foreach (var c in "*" + data + "*")
        {
            if (elements.Count > 0)
            {
                elements.Add(1); // inter-character gap (a narrow space)
            }

            elements.AddRange(Patterns[c].Select(p => p == 'w' ? 3 : 1));
        }

        return new LinearBarcode(BarcodeSymbology.Code39, elements);
    }
}

/// <summary>QR code via QRCoder, error correction level M, without the quiet zone.</summary>
public static class Qr
{
    public static bool[,] Encode(string text)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        var matrix = data.ModuleMatrix;
        const int quiet = 4;
        var size = matrix.Count - (2 * quiet);
        var result = new bool[size, size];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                result[y, x] = matrix[y + quiet][x + quiet];
            }
        }

        return result;
    }
}
