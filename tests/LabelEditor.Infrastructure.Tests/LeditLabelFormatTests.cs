using System.Text;
using LabelEditor.Application.Abstractions;
using LabelEditor.Domain.Entries;
using LabelEditor.Domain.Geometry;
using LabelEditor.Domain.Layout;
using LabelEditor.Domain.Text;
using LabelEditor.Infrastructure.Ledit;

namespace LabelEditor.Infrastructure.Tests;

public class LeditLabelFormatTests
{
    private readonly LeditLabelFormat _format = new();

    private async Task<LabelLayout> ReadAsync(string name)
    {
        await using var stream = File.OpenRead(TestData.Label(name));
        return await _format.ReadAsync(stream);
    }

    private async Task<LabelLayout> ReadBytesAsync(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return await _format.ReadAsync(stream);
    }

    [Theory]
    [MemberData(nameof(TestData.AllLabelNames), MemberType = typeof(TestData))]
    public async Task Every_known_label_round_trips_without_losing_information(string name)
    {
        var original = await ReadAsync(name);

        var reread = await ReadBytesAsync(_format.Serialize(original));

        Assert.Equal(TestData.WithoutIds(original), TestData.WithoutIds(reread));
    }

    [Theory]
    [MemberData(nameof(TestData.AllLabelNames), MemberType = typeof(TestData))]
    public async Task Labels_saved_by_ledit_are_reproduced_byte_for_byte(string name)
    {
        var bytes = await File.ReadAllBytesAsync(TestData.Label(name));
        var text = Encoding.Latin1.GetString(bytes);
        var writtenByLedit = text.Split("\r\n").Length == 3 && !text.Contains('\t', StringComparison.Ordinal) && !text.Contains("<Timestamp>", StringComparison.Ordinal);
        if (!writtenByLedit)
        {
            return; // Hand-edited / HTML-editor files: covered by the semantic round trip above.
        }

        var written = _format.Serialize(await ReadBytesAsync(bytes));

        Assert.Equal(text, Encoding.Latin1.GetString(written));
    }

    [Fact]
    public async Task Reads_coordinates_with_y_pointing_down()
    {
        var layout = await ReadAsync("Plats128C_V3.xml");

        Assert.Equal(100_000, layout.Header.Shape.Width);
        Assert.Equal(60_000, layout.Header.Shape.Height);
        Assert.Equal(1000, layout.Header.GridSize);
        var ellipse = layout.Entries.OfType<EllipseEntry>().First();
        Assert.Equal(new LabelRect(50_000, 53_000, 7_000, 7_000), ellipse.Bounds); // left=00050000 top=-0053000 w=7000 h=-7000
    }

    [Fact]
    public async Task Reads_text_entries_with_fields_rotation_and_barcode()
    {
        var layout = await ReadAsync("Plats128C_V3.xml");
        var barcode = layout.Entries.OfType<TextEntry>().First();

        Assert.True(barcode.IsBarcode);
        Assert.Equal("C28N", barcode.Font.FaceName);
        Assert.Equal(TextRotation.Cw90, barcode.Font.Rotation);
        Assert.Equal(280, barcode.Options.FontSize);
        var element = Assert.Single(barcode.Elements);
        Assert.Equal(new DataFieldReference(45836), element.Source);
        Assert.Equal("123", element.PreviewText);
    }

    [Fact]
    public async Task Reads_literal_text_and_legacy_v132_elements()
    {
        var layout = await ReadAsync("LayoutNest.xml");

        Assert.Equal(new FormatVersion(1, 32), layout.Header.Version);
        var first = layout.Entries.OfType<TextEntry>().First();
        var element = Assert.Single(first.Elements);
        Assert.Equal(new LiteralText("BIESSENEST"), element.Source);
        Assert.Equal(0, element.First);
        Assert.Null(element.DeleteFirst);
        Assert.Null(first.Options.TextColor);
    }

    [Fact]
    public async Task Reads_every_entry_kind_of_the_stock_layout()
    {
        var layout = await ReadAsync("LayoutDef.xml");

        var kinds = layout.Entries.Select(e => e.Kind).ToHashSet();
        Assert.Contains(EntryKind.Bitmap, kinds);
        Assert.Contains(EntryKind.LabelGraphic, kinds);
        Assert.Contains(EntryKind.Line, kinds);
        Assert.Contains(EntryKind.CornerLogo, kinds);
        Assert.Contains(EntryKind.DataRecord, kinds);
        Assert.DoesNotContain(EntryKind.Unknown, kinds);
        var logo = layout.Entries.OfType<CornerLogoEntry>().Single();
        Assert.Equal("Top_Edge", logo.TextTop);
        Assert.Equal(8, logo.EdgeThicknessPercent);
    }

    [Fact]
    public async Task Drops_timestamps_added_by_the_html_editor()
    {
        var layout = await ReadAsync("html-editor-export.xml");

        var text = Encoding.Latin1.GetString(_format.Serialize(layout));

        Assert.DoesNotContain("Timestamp", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Keeps_unknown_entries_verbatim()
    {
        const string xml = "<?xml version=\"1.0\" encoding=\"ISO-8859-1\"?>\r\n<LabelLayout><Header description=\"General information\"><Version majorVersion=\"1\" minorVersion=\"34\"/><Miscellaneous default=\"0\" unit=\"0\" grid=\"0\"/><RectLabel type=\"1\"><Width><![CDATA[100000]]></Width><Height><![CDATA[-60000]]></Height></RectLabel></Header><Entries description=\"Label elements\"><FutureEntry type=\"42\"><Field left=\"00001000\" top=\"-0002000\" width=\"00003000\" height=\"-0004000\"/><Magic><![CDATA[x]]></Magic></FutureEntry></Entries></LabelLayout>\r\n";

        var layout = await ReadBytesAsync(Encoding.Latin1.GetBytes(xml));

        var unknown = Assert.IsType<UnknownEntry>(Assert.Single(layout.Entries));
        Assert.Equal(new LabelRect(1000, 2000, 3000, 4000), unknown.Bounds);
        Assert.Equal(xml, Encoding.Latin1.GetString(_format.Serialize(layout)));
    }

    [Fact]
    public void Refuses_text_that_iso_8859_1_cannot_store()
    {
        var layout = LabelLayout.CreateNew().AddEntry(new TextEntry
        {
            Id = EntryId.New(),
            Bounds = new LabelRect(0, 0, 10_000, 5_000),
            Elements = [TextElement.ForLiteral("Количество")],
        });

        Assert.Throws<LabelDocumentException>(() => _format.Serialize(layout));
    }

    [Fact]
    public async Task Rejects_files_that_are_not_label_layouts()
    {
        var bytes = Encoding.Latin1.GetBytes("<?xml version=\"1.0\"?><Partslist/>");

        await Assert.ThrowsAsync<LabelDocumentException>(() => ReadBytesAsync(bytes));
    }

    [Theory]
    [InlineData(84_000, "00084000")]
    [InlineData(-14_000, "-0014000")]
    [InlineData(0, "00000000")]
    [InlineData(-310, "-0000310")]
    [InlineData(275_000, "00275000")]
    public void Formats_field_numbers_like_ledit(int value, string expected) =>
        Assert.Equal(expected, LeditNames.FormatFieldNumber(value));
}
