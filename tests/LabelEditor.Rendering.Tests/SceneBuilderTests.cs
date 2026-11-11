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

        Assert.Equal("Kund: [CabCode]", Assert.IsType<TextItem>(Assert.Single(scene.Items)).Text);
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
    public void Qr_font_produces_a_matrix_code()
    {
        var entry = Text(45836, face: "QR", barcode: 65536, preview: "115632793");

        var scene = Builder.Build(LabelLayout.CreateNew().AddEntry(entry));

        Assert.IsType<MatrixBarcodeItem>(Assert.Single(scene.Items));
    }

    [Fact]
    public void Data_records_are_not_drawn()
    {
        Assert.Empty(Builder.Build(LabelLayout.CreateNew()).Items);
    }
}
