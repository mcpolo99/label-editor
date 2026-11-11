using LabelEditor.Application;
using LabelEditor.Application.Documents;
using LabelEditor.Application.Editing;
using LabelEditor.Application.Localization;
using LabelEditor.Domain.Entries;
using LabelEditor.Domain.Fields;
using LabelEditor.Domain.Geometry;
using LabelEditor.Domain.Layout;
using LabelEditor.Domain.Text;
using LabelEditor.Presentation.Services;
using LabelEditor.Presentation.ViewModels;
using LabelEditor.Presentation.ViewModels.Properties;

namespace LabelEditor.Presentation.Tests;

public class TextPropertiesViewModelTests
{
    private sealed class NoFonts : IFontCatalog
    {
        public IReadOnlyList<string> FontFamilies => ["Arial"];
    }

    private static readonly FieldCatalog Catalog = new(new FieldCategory("root", TextReference.Literal("r"))
    {
        Fields =
        [
            new DataFieldDefinition(45804, "FinalLength", new TextReference(95, 9010, "L")),
            new DataFieldDefinition(45808, "QMin", new TextReference(95, 9014, "Quantità")),
        ],
    });

    private static (EditorSession Session, TextEntry Entry, TextPropertiesViewModel Panel) Setup()
    {
        var entry = new TextEntry
        {
            Id = EntryId.New(),
            Bounds = new LabelRect(0, 0, 30_000, 5_000),
            Elements = [TextElement.ForField(45804, "260")],
        };
        var session = new EditorSession(LabelLayout.CreateNew().AddEntry(entry));
        var context = new EditorContext(new EntryClipboard(), new NoFonts())
        {
            Resolver = new ReferenceData(Catalog, TextCatalog.Empty, []).CreateResolver(),
        };
        var panel = new TextPropertiesViewModel(session, entry, context);
        return (session, entry, panel);
    }

    private static TextElement ElementIn(EditorSession session, EntryId id) => ((TextEntry)session.Layout.Find(id)!).Elements[0];

    [Fact]
    public void Field_picker_lists_fields_by_element_name()
    {
        var (_, _, panel) = Setup();

        Assert.Equal(["FinalLength", "QMin"], panel.FieldOptions.Select(o => o.Name));
        Assert.Equal("FinalLength", panel.Elements[0].SelectedField?.Name);
    }

    [Fact]
    public void Choosing_another_field_retargets_the_element_as_one_undo_step()
    {
        var (session, entry, panel) = Setup();
        var element = panel.Elements[0];

        element.SelectedField = panel.FieldOptions.Single(o => o.Name == "QMin");

        var changed = ElementIn(session, entry.Id);
        Assert.Equal(new DataFieldReference(45808), changed.Source);
        Assert.Null(changed.PreviewText); // the old field's sample value no longer applies
        session.Undo();
        Assert.Equal(new DataFieldReference(45804), ElementIn(session, entry.Id).Source);
    }

    [Fact]
    public void Clearing_the_search_box_keeps_the_current_field()
    {
        var (session, entry, panel) = Setup();

        panel.Elements[0].SelectedField = null;

        Assert.Equal(new DataFieldReference(45804), ElementIn(session, entry.Id).Source);
        Assert.False(session.History.CanUndo);
    }

    [Fact]
    public void An_element_can_switch_between_field_and_fixed_text()
    {
        var (session, entry, panel) = Setup();

        panel.Elements[0].IsLiteral = true;
        Assert.Equal(new LiteralText("FinalLength"), ElementIn(session, entry.Id).Source);

        panel.Elements[0].IsField = true;
        Assert.Equal(new DataFieldReference(45804), ElementIn(session, entry.Id).Source);
    }
}
