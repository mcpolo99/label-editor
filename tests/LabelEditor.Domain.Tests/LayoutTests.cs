using LabelEditor.Domain.Editing;
using LabelEditor.Domain.Entries;
using LabelEditor.Domain.Fields;
using LabelEditor.Domain.Geometry;
using LabelEditor.Domain.Layout;
using LabelEditor.Domain.Text;

namespace LabelEditor.Domain.Tests;

public class LayoutTests
{
    private static EllipseEntry Box(int x, int y, int w = 10, int h = 10) => new() { Id = EntryId.New(), Bounds = new LabelRect(x, y, w, h) };

    private static LabelLayout With(params LabelEntry[] entries) => LabelLayout.CreateNew().AddEntries(entries);

    [Fact]
    public void Layouts_compare_by_value()
    {
        var a = Box(1, 2);
        var empty = LabelLayout.CreateNew();
        Assert.Equal(empty.AddEntry(a), empty.AddEntry(a));
        Assert.NotEqual(empty.AddEntry(a), empty.AddEntry(a with { Bounds = a.Bounds.Offset(1, 0) }));
    }

    [Fact]
    public void Align_left_moves_others_to_the_dominant_entry()
    {
        var dominant = Box(30, 0);
        var other = Box(70, 40);

        var result = LayoutArrangement.Align(With(dominant, other), [dominant.Id, other.Id], Alignment.Left);

        Assert.Equal(30, result.Find(other.Id)!.Bounds.X);
        Assert.Equal(40, result.Find(other.Id)!.Bounds.Y);
        Assert.Equal(dominant, result.Find(dominant.Id));
    }

    [Fact]
    public void Center_in_label_moves_the_selection_as_a_group()
    {
        var layout = LabelLayout.CreateNew(100, 60);
        var a = Box(0, 0);
        var b = Box(20, 0);

        var result = LayoutArrangement.Align(layout.AddEntries([a, b]), [a.Id, b.Id], Alignment.CenterHorizontallyInLabel);

        Assert.Equal(35, result.Find(a.Id)!.Bounds.X);
        Assert.Equal(55, result.Find(b.Id)!.Bounds.X);
    }

    [Fact]
    public void Distribute_makes_gaps_equal_and_keeps_the_outer_entries()
    {
        var a = Box(0, 0);
        var b = Box(15, 0);
        var c = Box(90, 0);

        var result = LayoutArrangement.Distribute(With(a, b, c), [a.Id, b.Id, c.Id], Distribution.Horizontal);

        Assert.Equal(0, result.Find(a.Id)!.Bounds.X);
        Assert.Equal(45, result.Find(b.Id)!.Bounds.X);
        Assert.Equal(90, result.Find(c.Id)!.Bounds.X);
    }

    [Fact]
    public void Match_size_keeps_line_direction()
    {
        var dominant = Box(0, 0, 40, 10);
        var line = new LineEntry { Id = EntryId.New(), Bounds = new LabelRect(50, 50, -10, 0) };

        var result = LayoutArrangement.MatchSize(With(dominant, line), [dominant.Id, line.Id], SizeMatch.Width);

        Assert.Equal(-40, result.Find(line.Id)!.Bounds.Width);
    }

    [Fact]
    public void Z_order_commands_keep_the_data_record_first()
    {
        var a = Box(0, 0);
        var b = Box(5, 5);
        var layout = With(a, b);

        var front = layout.BringToFront(new HashSet<EntryId> { a.Id });
        var back = front.SendToBack(new HashSet<EntryId> { a.Id });

        Assert.Equal([EntryKind.DataRecord, EntryKind.Ellipse, EntryKind.Ellipse], front.Entries.Select(e => e.Kind));
        Assert.Equal(a.Id, front.Entries[^1].Id);
        Assert.IsType<DataRecordEntry>(back.Entries[0]);
        Assert.Equal(a.Id, back.Entries[1].Id);
    }

    [Fact]
    public void Hit_testing_returns_the_topmost_entry_and_finds_thin_lines()
    {
        var below = Box(0, 0, 50, 50);
        var above = Box(10, 10, 10, 10);
        var line = new LineEntry { Id = EntryId.New(), Bounds = new LabelRect(0, 100, 100, 0) };
        var layout = With(below, above, line);

        Assert.Equal(above.Id, HitTesting.EntryAt(layout, new LabelPoint(15, 15), 0)?.Id);
        Assert.Equal(below.Id, HitTesting.EntryAt(layout, new LabelPoint(40, 40), 0)?.Id);
        Assert.Equal(line.Id, HitTesting.EntryAt(layout, new LabelPoint(50, 102), 3)?.Id);
        Assert.Null(HitTesting.EntryAt(layout, new LabelPoint(50, 110), 3));
    }

    [Fact]
    public void Text_composer_applies_trimming_and_upper_case()
    {
        var entry = new TextEntry
        {
            Id = EntryId.New(),
            Bounds = default,
            Options = new TextOptions { UpperCase = true },
            Elements =
            [
                TextElement.ForLiteral("L: "),
                TextElement.ForField(45804, "abcdef") with { DeleteFirst = 1, DeleteLast = 1, Count = 3 },
            ],
        };

        Assert.Equal("L: BCD", TextComposer.Compose(entry, (_, e) => e.PreviewText));
    }

    [Theory]
    [InlineData(65537, BarcodeType.Code128)]
    [InlineData(65538, BarcodeType.Code39)]
    [InlineData(65539, BarcodeType.Code128C)]
    [InlineData(65548, BarcodeType.QrCode)]
    public void Barcode_settings_carry_the_symbology_in_the_low_word(int raw, BarcodeType expected)
    {
        var settings = new BarcodeSettings(raw);

        Assert.True(settings.IsBarcode);
        Assert.Equal(expected, settings.Type);
        Assert.Equal(raw, BarcodeSettings.For(expected).Raw);
    }

    [Fact]
    public void Unknown_symbology_codes_are_preserved()
    {
        var settings = new BarcodeSettings(65536 + 7);

        Assert.Null(settings.Type);
        Assert.Equal(7, settings.SymbologyCode);
        Assert.Equal(65536 + 7, settings.WithBarcode(false).WithBarcode(true).Raw);
    }

    [Fact]
    public void Font_rotation_maps_ledit_escapement()
    {
        Assert.Equal(TextRotation.Cw90, (FontSpec.Default with { Escapement = 2700 }).Rotation);
        Assert.Equal(900, FontSpec.Default.WithRotation(TextRotation.Ccw90).Escapement);
    }

    [Fact]
    public void Catalog_names_fields_by_element_name()
    {
        var root = new FieldCategory("root", TextReference.Literal("r"))
        {
            Fields = [new DataFieldDefinition(45808, "QMin", new TextReference(95, 9014, "Quantità"))],
        };

        Assert.Equal("QMin", new FieldCatalog(root).Find(45808)?.Name);
    }
}
