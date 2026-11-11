using LabelEditor.Application;
using LabelEditor.Application.Documents;
using LabelEditor.Application.Editing;
using LabelEditor.Application.Localization;
using LabelEditor.Application.Preview;
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

    /// <summary>Stands in for the field dialog: answers with a queued field id (null = cancel).</summary>
    private sealed class FieldDialog : IUserInteraction
    {
        public Queue<int?> Answers { get; } = new();

        public List<int?> OpenedWith { get; } = [];

        public Task<int?> PickFieldAsync(FieldTextResolver resolver, int? currentFieldId)
        {
            OpenedWith.Add(currentFieldId);
            return Task.FromResult(Answers.Dequeue());
        }

        public Task<string?> PickLabelToOpenAsync(string? initialFolder) => throw new NotSupportedException();

        public Task<string?> PickLabelSaveLocationAsync(string suggestedName, string? initialFolder) => throw new NotSupportedException();

        public Task<string?> PickBitmapAsync(string? initialFolder) => throw new NotSupportedException();

        public Task<string?> PickPreviewDataAsync(string? initialFolder) => throw new NotSupportedException();

        public Task<SaveChangesAnswer> AskSaveChangesAsync(string documentTitle) => throw new NotSupportedException();

        public Task<ReplaceAnswer> AskReplaceExistingAsync(string path, string? protectedReason) => throw new NotSupportedException();

        public Task ShowErrorAsync(string title, string message) => throw new NotSupportedException();
    }

    private static readonly FieldCatalog Catalog = new(new FieldCategory("root", TextReference.Literal("r"))
    {
        Children =
        [
            new FieldCategory("General", TextReference.Literal("General")) { Fields = [new DataFieldDefinition(FieldCatalog.RawTextId, "RawText", TextReference.Literal("Raw text"))] },
            new FieldCategory("PartData", TextReference.Literal("Piece data"))
            {
                Fields =
                [
                    new DataFieldDefinition(45804, "FinalLength", new TextReference(95, 9010, "L")),
                    new DataFieldDefinition(45808, "QMin", new TextReference(95, 9014, "Quantità")),
                ],
            },
        ],
    });

    private static (EditorSession Session, TextEntry Entry, TextPropertiesViewModel Panel, FieldDialog Dialog) Setup()
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
        var dialog = new FieldDialog();
        return (session, entry, new TextPropertiesViewModel(session, entry, context, dialog), dialog);
    }

    private static TextElement ElementIn(EditorSession session, EntryId id) => ((TextEntry)session.Layout.Find(id)!).Elements[0];

    [Fact]
    public async Task Choosing_a_field_in_the_dialog_retargets_the_element_as_one_undo_step()
    {
        var (session, entry, panel, dialog) = Setup();
        dialog.Answers.Enqueue(45808);

        await panel.Elements[0].ChooseFieldCommand.ExecuteAsync(null);

        Assert.Equal([45804], dialog.OpenedWith); // the dialog opens at the current field
        var changed = ElementIn(session, entry.Id);
        Assert.Equal(new DataFieldReference(45808), changed.Source);
        Assert.Null(changed.PreviewText); // the old field's sample value no longer applies
        panel.Refresh(session.Layout.Find(entry.Id)!); // the document view model does this on every session change
        Assert.Equal("QMin", panel.Elements[0].FieldName);
        session.Undo();
        Assert.Equal(new DataFieldReference(45804), ElementIn(session, entry.Id).Source);
    }

    [Fact]
    public async Task Cancelling_the_dialog_changes_nothing()
    {
        var (session, entry, panel, dialog) = Setup();
        dialog.Answers.Enqueue(null);

        await panel.Elements[0].ChooseFieldCommand.ExecuteAsync(null);

        Assert.Equal(new DataFieldReference(45804), ElementIn(session, entry.Id).Source);
        Assert.False(session.History.CanUndo);
    }

    [Fact]
    public void Switching_fixed_text_back_to_a_field_asks_which_field()
    {
        var (session, entry, panel, dialog) = Setup();

        panel.Elements[0].IsLiteral = true;
        Assert.Equal(new LiteralText("FinalLength"), ElementIn(session, entry.Id).Source);

        dialog.Answers.Enqueue(45808);
        panel.Elements[0].IsField = true;
        Assert.Equal(new DataFieldReference(45808), ElementIn(session, entry.Id).Source);
    }

    [Fact]
    public void Cancelling_the_switch_to_a_field_keeps_fixed_text()
    {
        var (session, entry, panel, dialog) = Setup();
        panel.Elements[0].IsLiteral = true;

        dialog.Answers.Enqueue(null);
        panel.Elements[0].IsField = true;

        Assert.IsType<LiteralText>(ElementIn(session, entry.Id).Source);
        Assert.True(panel.Elements[0].IsLiteral);
    }

    [Fact]
    public async Task Choosing_raw_text_in_the_tree_turns_the_element_into_fixed_text()
    {
        var (session, entry, panel, dialog) = Setup();
        dialog.Answers.Enqueue(FieldCatalog.RawTextId);

        await panel.Elements[0].ChooseFieldCommand.ExecuteAsync(null);

        Assert.IsType<LiteralText>(ElementIn(session, entry.Id).Source);
    }

    [Fact]
    public void Field_tree_reveals_the_current_field_inside_its_category()
    {
        var picker = new FieldPickerViewModel();
        picker.Load(new ReferenceData(Catalog, TextCatalog.Empty, []).CreateResolver());

        Assert.True(picker.Reveal(45808));

        Assert.Equal("QMin", picker.SelectedNode?.Name);
        Assert.Equal(45808, picker.SelectedFieldId);
        Assert.True(picker.Nodes.Single(n => n.Name == "Piece data").IsExpanded);
        Assert.False(picker.Nodes.Single(n => n.Name == "General").IsExpanded);
    }

    [Fact]
    public void Filtering_the_tree_expands_matching_categories()
    {
        var picker = new FieldPickerViewModel();
        picker.Load(new ReferenceData(Catalog, TextCatalog.Empty, []).CreateResolver());

        picker.Filter = "qmin";

        var category = Assert.Single(picker.Nodes);
        Assert.True(category.IsExpanded);
        Assert.Equal("QMin", Assert.Single(category.Children).Name);
    }
}
