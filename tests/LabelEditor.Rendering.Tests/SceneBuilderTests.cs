using LabelEditor.Application.Localization;
using LabelEditor.Application.Preview;
using LabelEditor.Domain.Entries;
using LabelEditor.Domain.Fields;
using LabelEditor.Domain.Geometry;
using LabelEditor.Domain.Layout;
using LabelEditor.Domain.Text;
using LabelEditor.Rendering.Scene;

namespace LabelEditor.Rendering.Tests;

public class SceneBuilderTests
{
    private static readonly FieldCatalog Catalog = new(new FieldCategory("root", TextReference.Literal("r"))
    {
        Fields =
        [
            new DataFieldDefinition(45836, "CabInfo", new TextReference(95, 9053, "Descrizione 4")),
            new DataFieldDefinition(45831, "CabCode", new TextReference(95, 9048, "Descrizione 3")),
        ],
    });

    private static readonly LabelSceneBuilder Builder = new(new FieldTextResolver(Catalog, TextCatalog.Empty));

    private static TextEntry Text(int fieldId, string face = "Arial", int barcode = 0, string? preview = null) => new()
    {
        Id = EntryId.New(),
        Bounds = new LabelRect(1_000, 2_000, 40_000, 6_000),
        Font = FontSpec.Default with { FaceName = face, Weight = 700 },
        Options = new TextOptions { FontSize = 100, Barcode = new BarcodeSettings(barcode) },
        Elements = [TextElement.ForLiteral("Kund: "), TextElement.ForField(fieldId, preview)],
    };

    [Fact]
    public void Edge_indicator_shows_its_sample_texts_in_design_view()
    {
        var edges = new CornerLogoEntry { Id = EntryId.New(), Bounds = new LabelRect(0, 0, 30_000, 20_000), TextTop = "xx", TextBottom = "xx", TextLeft = string.Empty, TextRight = "x" };

        var item = Assert.IsType<CornerLogoItem>(Assert.Single(Builder.Build(LabelLayout.CreateNew().AddEntry(edges)).Items));

        Assert.Equal(("xx", "xx", string.Empty, "x"), (item.Top, item.Bottom, item.Left, item.Right));
        Assert.Null(item.PartAspect);
    }

    [Fact]
    public void Edge_indicator_shows_the_parts_edges_from_the_record_and_no_edge_where_it_has_none()
    {
        var edges = new CornerLogoEntry { Id = EntryId.New(), Bounds = new LabelRect(0, 0, 30_000, 20_000), EdgeInfo = 0 };
        var record = new PreviewRecord("P1", [new("MatEdgeUp", "STD-PALL"), new("MatEdgeLo", "P3"), new("ColEdgeR", "white"), new("L", "800"), new("W", "400")]);

        var item = Assert.IsType<CornerLogoItem>(Assert.Single(Builder.Build(LabelLayout.CreateNew().AddEntry(edges), record).Items));

        // Material is printed; the right side only has a colour, so it shows no edge (like the left one).
        Assert.Equal(("STD-PALL", "P3", string.Empty, string.Empty), (item.Top, item.Bottom, item.Left, item.Right));
        Assert.Equal(2.0, item.PartAspect);

        var byColour = Assert.IsType<CornerLogoItem>(Builder.Build(LabelLayout.CreateNew().AddEntry(edges with { EdgeInfo = 1 }), record).Items.Single());
        Assert.Equal("white", byColour.Right);
    }

    [Fact]
    public void Text_uses_record_values_and_point_sizes()
    {
        var record = new PreviewRecord("P1", [new("CabCode", "Customer 1")]);

        var scene = Builder.Build(LabelLayout.CreateNew().AddEntry(Text(45831)), record);

        var text = Assert.IsType<TextItem>(Assert.Single(scene.Items));
        Assert.Equal("Kund: Customer 1", text.Text);
        Assert.True(text.Bold);
        Assert.Equal(3527.8, text.EmSize!.Value, 1); // 10 pt = 3.53 mm
    }

    [Fact]
    public void Text_without_data_shows_the_field_name_placeholder()
    {
        var scene = Builder.Build(LabelLayout.CreateNew().AddEntry(Text(45831)));

        var text = Assert.IsType<TextItem>(Assert.Single(scene.Items));
        Assert.Equal("Kund: [CabCode]", text.Text);
        Assert.True(text.HasPlaceholder);
    }

    [Fact]
    public void Sample_values_and_record_values_are_real_text_not_placeholders()
    {
        Assert.False(Assert.IsType<TextItem>(Builder.Build(LabelLayout.CreateNew().AddEntry(Text(45831, preview: "Customer 1"))).Items.Single()).HasPlaceholder);
        var record = new PreviewRecord("P1", []);
        Assert.False(Assert.IsType<TextItem>(Builder.Build(LabelLayout.CreateNew().AddEntry(Text(45831)), record).Items.Single()).HasPlaceholder);
    }

    [Fact]
    public void Barcode_entries_are_encoded_with_sample_data_while_designing()
    {
        var entry = Text(45836, face: "C28N", barcode: 65539) with { Elements = [TextElement.ForField(45836)] };

        var scene = Builder.Build(LabelLayout.CreateNew().AddEntry(entry));

        var barcode = Assert.IsType<LinearBarcodeItem>(Assert.Single(scene.Items));
        Assert.Equal("123456789", barcode.Data);
    }

    [Fact]
    public void Symbology_code_12_produces_a_qr_code()
    {
        var entry = Text(45836, face: "Arial", barcode: 65548, preview: "115632793");

        var scene = Builder.Build(LabelLayout.CreateNew().AddEntry(entry));

        Assert.IsType<MatrixBarcodeItem>(Assert.Single(scene.Items));
    }

    [Fact]
    public void Fields_missing_from_the_record_print_nothing_but_fixed_text_stays()
    {
        var record = new PreviewRecord("P1", [new("SomethingElse", "x")]);

        var scene = Builder.Build(LabelLayout.CreateNew().AddEntry(Text(45831, preview: "sample")), record);

        Assert.Equal("Kund: ", Assert.IsType<TextItem>(Assert.Single(scene.Items)).Text);
    }

    [Fact]
    public void Barcodes_without_data_in_the_record_are_not_drawn()
    {
        var entry = Text(45836, face: "C28N", barcode: 65539, preview: "123") with { Elements = [TextElement.ForField(45836, "123")] };

        var scene = Builder.Build(LabelLayout.CreateNew().AddEntry(entry), new PreviewRecord("P1", []));

        Assert.Empty(scene.Items);
    }

    [Fact]
    public void Data_records_are_not_drawn()
    {
        Assert.Empty(Builder.Build(LabelLayout.CreateNew()).Items);
    }
}
