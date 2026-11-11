using LabelEditor.Rendering.Barcodes;

namespace LabelEditor.Rendering.Tests;

public class BarcodeTests
{
    [Fact]
    public void Code128_patterns_are_valid_symbols()
    {
        Assert.Equal(107, Code128.Patterns.Count);
        Assert.Equal(Code128.Patterns.Count, Code128.Patterns.Distinct().Count());
        for (var value = 0; value < 106; value++)
        {
            var widths = Code128.Patterns[value].Select(c => c - '0').ToArray();
            Assert.Equal(11, widths.Sum());

            // Code 128 symbols have even bar parity: the bar widths (odd positions) add up to an even number.
            Assert.Equal(0, (widths[0] + widths[2] + widths[4]) % 2);
        }

        Assert.Equal(13, Code128.Patterns[Code128.Stop].Select(c => c - '0').Sum());
    }

    [Fact]
    public void Code128_uses_code_b_for_text_and_computes_the_checksum()
    {
        var values = Code128.EncodeValues("PJJ123C");

        // Start B, P J J 1 2 3 C, checksum (104 + 48·1 + 42·2 + 42·3 + 17·4 + 18·5 + 19·6 + 35·7) mod 103 = 55, stop.
        Assert.Equal([104, 48, 42, 42, 17, 18, 19, 35, 55, 106], values);
    }

    [Fact]
    public void Code128_packs_digit_runs_with_code_c()
    {
        var values = Code128.EncodeValues("115632793")!;

        Assert.Equal(Code128.StartC, values[0]);
        Assert.Equal([11, 56, 32, 79], values.Skip(1).Take(4));
        Assert.Equal(Code128.SwitchToB, values[5]);
        Assert.Equal('3' - 32, values[6]);
    }

    [Fact]
    public void Code128_rejects_non_ascii() => Assert.Null(Code128.Encode("Bølviken"));

    [Fact]
    public void Code39_patterns_have_three_wide_elements()
    {
        Assert.Equal(44, Code39.Patterns.Count);
        Assert.All(Code39.Patterns.Values, p => Assert.Equal(3, p.Count(c => c == 'w')));
        Assert.Equal(Code39.Patterns.Count, Code39.Patterns.Values.Distinct().Count());
    }

    [Fact]
    public void Code39_wraps_data_in_start_stop_and_adds_the_mod43_check()
    {
        var plain = Code39.Encode("AB", addCheckCharacter: false)!;
        var withCheck = Code39.Encode("AB", addCheckCharacter: true)!;

        Assert.Equal((4 * 9) + 3, plain.Elements.Count); // * A B * plus three gaps
        Assert.Equal((5 * 9) + 4, withCheck.Elements.Count);
        Assert.Null(Code39.Encode("a_b", false));
    }

    [Fact]
    public void Qr_encodes_a_square_matrix_with_finder_patterns()
    {
        var modules = Qr.Encode("115632793");

        Assert.Equal(21, modules.GetLength(0)); // version 1
        Assert.Equal(modules.GetLength(0), modules.GetLength(1));
        Assert.True(modules[0, 0] && modules[0, 6] && modules[6, 0] && modules[6, 6]);
    }

    [Theory]
    [InlineData("C28N", BarcodeSymbology.Code128)]
    [InlineData("A39N", BarcodeSymbology.Code39)]
    [InlineData("QR", BarcodeSymbology.QrCode)]
    public void Symbology_follows_the_barcode_font(string font, BarcodeSymbology expected) =>
        Assert.Equal(expected, BarcodeFonts.SymbologyFor(font));
}
