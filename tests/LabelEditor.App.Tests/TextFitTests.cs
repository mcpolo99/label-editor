using Avalonia.Headless.XUnit;
using LabelEditor.App.Rendering;
using LabelEditor.Application.Localization;
using LabelEditor.Application.Preview;
using LabelEditor.Domain.Entries;
using LabelEditor.Domain.Fields;
using LabelEditor.Domain.Geometry;
using LabelEditor.Domain.Layout;
using LabelEditor.Domain.Text;
using LabelEditor.Rendering.Scene;

namespace LabelEditor.App.Tests;

/// <summary>Text measured at print size against its field, with the real fonts.</summary>
public class TextFitTests
{
    // 10 pt = 3528 layout units (1/1000 mm).
    private static TextItem Text(string text, int width, double? em = 3_528, TextRotation rotation = TextRotation.None, bool wrap = false, int height = 5_000) =>
        new(EntryId.New(), new LabelRect(0, 0, width, height), text, "Arial", em, false, false, false, false, rotation, new RgbColor(0, 0, 0), wrap);

    [AvaloniaFact]
    public void A_short_text_fits_and_a_long_one_does_not()
    {
        Assert.False(TextFit.Overflows(Text("L: 800", 40_000)));
        Assert.True(TextFit.Overflows(Text("A customer name that is far too long for this field", 40_000)));
    }

    [AvaloniaFact]
    public void Placeholders_of_fields_without_a_sample_value_are_not_judged()
    {
        Assert.False(TextFit.Overflows(Text("[A_field_name_that_is_long]", 5_000) with { HasPlaceholder = true }));
    }

    [AvaloniaFact]
    public void Texts_that_scale_to_their_field_always_fit()
    {
        Assert.False(TextFit.Overflows(Text("A customer name that is far too long for this field", 10_000, em: null)));
    }

    [AvaloniaFact]
    public void Rotated_text_is_measured_along_its_direction()
    {
        // 6 mm wide, 60 mm tall field, text running upwards: the 60 mm count.
        var upright = Text("Customer name 12345", 6_000, rotation: TextRotation.Ccw90, height: 60_000);

        Assert.False(TextFit.Overflows(upright));
        Assert.True(TextFit.Overflows(upright with { Rotation = TextRotation.None }));
    }

    [AvaloniaFact]
    public void Wrapped_text_overflows_only_when_its_lines_do_not_fit_the_height()
    {
        var text = "one two three four five six seven eight nine ten";

        Assert.False(TextFit.Overflows(Text(text, 30_000, wrap: true, height: 30_000)));
        Assert.True(TextFit.Overflows(Text(text, 30_000, wrap: true, height: 5_000)));
    }

    [AvaloniaFact]
    public void The_check_reports_each_entry_once_with_the_first_record_it_overflows_with()
    {
        var catalog = new FieldCatalog(new FieldCategory("root", TextReference.Literal("r"))
        {
            Fields = [new DataFieldDefinition(45831, "CabCode", new TextReference(95, 9048, "x"))],
        });
        var entry = new TextEntry
        {
            Id = EntryId.New(),
            Bounds = new LabelRect(0, 0, 40_000, 6_000),
            Options = new TextOptions { FontSize = 100 },
            Elements = [TextElement.ForField(45831, "Short")],
        };
        var layout = LabelLayout.CreateNew().AddEntry(entry);
        PreviewRecord[] records =
        [
            new("P1", [new("CabCode", "Fine")]),
            new("P2", [new("CabCode", "A customer name that is far too long for this field")]),
            new("P3", [new("CabCode", "Another customer name that is far too long as well")]),
        ];

        var found = new AvaloniaTextOverflowCheck().Find(layout, new FieldTextResolver(catalog, TextCatalog.Empty), records);

        var overflow = Assert.Single(found);
        Assert.Equal((entry.Id, "P2"), (overflow.EntryId, overflow.RecordKey));
    }
}
