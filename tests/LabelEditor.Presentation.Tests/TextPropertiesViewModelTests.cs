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


        public Task<string?> PickFolderAsync(string title, string? initialFolder) => Task.FromResult<string?>(null);

        public Task<SaveChangesAnswer> AskSaveChangesAsync(string documentTitle) => throw new NotSupportedException();

        public Task<ReplaceAnswer> AskReplaceExistingAsync(string path, string? protectedReason) => throw new NotSupportedException();

        public Task ShowErrorAsync(string title, string message) => throw new NotSupportedException();

        public Task ShowMessageAsync(string title, string message) => Task.CompletedTask;

        public Queue<IReadOnlyList<int>> MultiAnswers { get; } = new();

        public Task<IReadOnlyList<int>> PickFieldsAsync(FieldTextResolver resolver) => Task.FromResult(MultiAnswers.Dequeue());

        public Task<HistoryChoice?> ShowHistoryAsync(string labelTitle, IReadOnlyList<Application.Abstractions.LabelVersionInfo> versions) => throw new NotSupportedException();
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
    public async Task Switching_fixed_text_back_to_a_field_asks_which_field()
    {
        var (session, entry, panel, dialog) = Setup();

        panel.Elements[0].UseFixedTextCommand.Execute(null);
        Assert.Equal(new LiteralText("FinalLength"), ElementIn(session, entry.Id).Source);
        RefreshFromSession(panel, session, entry.Id);

        dialog.Answers.Enqueue(45808);
        await panel.Elements[0].UseDataFieldCommand.ExecuteAsync(null);
        Assert.Equal(new DataFieldReference(45808), ElementIn(session, entry.Id).Source);
    }

    [Fact]
    public async Task Cancelling_the_switch_to_a_field_keeps_fixed_text()
    {
        var (session, entry, panel, dialog) = Setup();
        panel.Elements[0].UseFixedTextCommand.Execute(null);
        RefreshFromSession(panel, session, entry.Id);

        dialog.Answers.Enqueue(null);
        await panel.Elements[0].UseDataFieldCommand.ExecuteAsync(null);

        Assert.IsType<LiteralText>(ElementIn(session, entry.Id).Source);
        Assert.True(panel.Elements[0].IsLiteral);
    }

    [Fact]
    public void Refreshing_a_piece_to_another_kind_never_opens_the_field_dialog()
    {
        // Selecting another entry re-binds the panel; that must not be taken as the user switching the kind.
        var (session, entry, panel, dialog) = Setup();
        panel.Elements[0].UseFixedTextCommand.Execute(null);
        RefreshFromSession(panel, session, entry.Id);
        session.Undo();
        RefreshFromSession(panel, session, entry.Id);

        Assert.Empty(dialog.OpenedWith);
        Assert.True(panel.Elements[0].IsField);
    }

    [Fact]
    public async Task Choosing_raw_text_in_the_tree_turns_the_element_into_fixed_text()
    {
        var (session, entry, panel, dialog) = Setup();
        dialog.Answers.Enqueue(FieldCatalog.RawTextId);

        await panel.Elements[0].ChooseFieldCommand.ExecuteAsync(null);

        Assert.IsType<LiteralText>(ElementIn(session, entry.Id).Source);
    }

    private static TextEntry EntryIn(EditorSession session, EntryId id) => (TextEntry)session.Layout.Find(id)!;

    private static string Describe(EditorSession session, EntryId id) =>
        string.Join(" ", EntryIn(session, id).Elements.Select(e => e.Source is LiteralText l ? $"\"{l.Text}\"" : $"[{((DataFieldReference)e.Source).FieldId}]"));

    private static void RefreshFromSession(TextPropertiesViewModel panel, EditorSession session, EntryId id) => panel.Refresh(EntryIn(session, id));

    [Fact]
    public async Task Several_fields_are_added_in_the_chosen_order_after_the_selected_piece()
    {
        var (session, entry, panel, dialog) = Setup();
        panel.SelectedElement = panel.Elements[0];
        dialog.MultiAnswers.Enqueue([45808, 45804]);

        await panel.AddFieldsCommand.ExecuteAsync(null);

        Assert.Equal("[45804] [45808] [45804]", Describe(session, entry.Id));
        session.Undo(); // one undo step for the whole batch
        Assert.Equal("[45804]", Describe(session, entry.Id));
    }

    [Fact]
    public async Task Raw_text_from_the_tree_adds_a_fixed_text_piece_so_fields_and_text_can_be_mixed()
    {
        var (session, entry, panel, dialog) = Setup();

        dialog.MultiAnswers.Enqueue([FieldCatalog.RawTextId]); // "Raw text" in the tree
        await panel.AddFieldsCommand.ExecuteAsync(null);
        RefreshFromSession(panel, session, entry.Id);
        panel.Elements[1].LiteralText = " / ";
        RefreshFromSession(panel, session, entry.Id);
        panel.SelectedElement = panel.Elements[0];
        panel.AddField(45808);
        RefreshFromSession(panel, session, entry.Id);

        Assert.Equal("[45804] [45808] \" / \"", Describe(session, entry.Id));
        Assert.Same(panel.Elements[1], panel.SelectedElement); // the new piece is selected for editing
        Assert.True(panel.Elements[1].IsSelected);
        Assert.False(panel.Elements[0].IsSelected);
    }

    [Fact]
    public void A_chip_can_be_removed_directly()
    {
        var (session, entry, panel, _) = Setup();
        panel.AddField(45808);
        RefreshFromSession(panel, session, entry.Id);

        panel.Elements[0].RemoveCommand.Execute(null);

        Assert.Equal("[45808]", Describe(session, entry.Id));
    }

    [Fact]
    public async Task Cancelling_the_add_fields_dialog_changes_nothing()
    {
        var (session, entry, panel, dialog) = Setup();
        dialog.MultiAnswers.Enqueue([]);

        await panel.AddFieldsCommand.ExecuteAsync(null);

        Assert.Equal("[45804]", Describe(session, entry.Id));
        Assert.False(session.History.CanUndo);
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
