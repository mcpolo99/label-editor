using LabelEditor.Application.Documents;
using LabelEditor.Application.Editing;
using LabelEditor.Domain.Entries;
using LabelEditor.Domain.Geometry;
using LabelEditor.Domain.Layout;
using LabelEditor.Presentation.Services;
using LabelEditor.Presentation.ViewModels;

namespace LabelEditor.Presentation.Tests;

public class DocumentCommandsTests
{
    private sealed class NoFonts : IFontCatalog
    {
        public IReadOnlyList<string> FontFamilies => [];
    }

    private static EllipseEntry Box(int x) => new() { Id = EntryId.New(), Bounds = new LabelRect(x, 0, 5_000, 5_000) };

    private static (DocumentViewModel Document, EllipseEntry[] Boxes) Setup()
    {
        EllipseEntry[] boxes = [Box(0), Box(10_000), Box(30_000)];
        var session = new EditorSession(LabelLayout.CreateNew().AddEntries(boxes));
        var context = new EditorContext(new EntryClipboard(), new NoFonts());
        return (new DocumentViewModel(session, context, null!), boxes);
    }

    [Theory]
    [InlineData(0, "SnapToGrid", false)]
    [InlineData(1, "SnapToGrid", true)]
    [InlineData(1, "AlignLeft", false)]
    [InlineData(2, "AlignLeft", true)]
    [InlineData(2, "SameSize", true)]
    [InlineData(2, "SpaceAcross", false)]
    [InlineData(3, "SpaceAcross", true)]
    [InlineData(1, "Front", true)]
    [InlineData(1, "ToggleLock", true)]
    public void Arrange_commands_are_enabled_only_when_the_selection_allows_them(int selected, string operation, bool expected)
    {
        var (document, boxes) = Setup();

        document.Session.Selection.Select(boxes.Take(selected).Select(b => b.Id));

        Assert.Equal(expected, document.ArrangeCommand.CanExecute(operation));
    }

    [Fact]
    public void Right_click_selects_an_unselected_entry_but_keeps_an_existing_multi_selection()
    {
        var (document, boxes) = Setup();
        document.Session.Selection.Select([boxes[0].Id, boxes[1].Id]);

        document.SelectForContextMenu(boxes[1].Id);
        Assert.Equal([boxes[0].Id, boxes[1].Id], document.Session.Selection.Ids);

        document.SelectForContextMenu(boxes[2].Id);
        Assert.Equal([boxes[2].Id], document.Session.Selection.Ids);

        document.SelectForContextMenu(null); // empty area: the selection stays
        Assert.Equal([boxes[2].Id], document.Session.Selection.Ids);
    }

    [Fact]
    public void Overlapping_entries_can_be_listed_and_picked()
    {
        var back = new EllipseEntry { Id = EntryId.New(), Bounds = new LabelRect(0, 0, 20_000, 20_000) };
        var front = new LineEntry { Id = EntryId.New(), Bounds = new LabelRect(0, 10_000, 20_000, 0) };
        var session = new EditorSession(LabelLayout.CreateNew().AddEntries([back, front]));
        var document = new DocumentViewModel(session, new EditorContext(new EntryClipboard(), new NoFonts()), null!);

        var stack = document.EntriesAt(new LabelPoint(5_000, 10_000), 100);

        Assert.Equal([front.Id, back.Id], stack.Select(i => i.Id));
        Assert.Equal(["Line", "Ellipse"], stack.Select(i => i.Kind));
        document.HighlightedEntry = back.Id;
        document.SelectEntry(back.Id);
        Assert.Equal([back.Id], session.Selection.Ids);
        Assert.Null(document.HighlightedEntry);
    }

    [Fact]
    public void Toggle_lock_locks_a_mixed_selection_and_unlocks_a_locked_one()
    {
        var (document, boxes) = Setup();
        document.Session.Selection.Select([boxes[0].Id, boxes[1].Id]);

        document.ArrangeCommand.Execute("ToggleLock");
        Assert.True(document.IsSelectionSizeLocked);

        document.ArrangeCommand.Execute("ToggleLock");
        Assert.False(document.IsSelectionSizeLocked);
    }

    [Fact]
    public void Command_state_follows_the_selection_and_clipboard()
    {
        var (document, boxes) = Setup();
        var changes = 0;
        document.CopyCommand.CanExecuteChanged += (_, _) => changes++;

        Assert.False(document.CopyCommand.CanExecute(null));
        Assert.False(document.PasteCommand.CanExecute(null));
        document.Session.Selection.Select(boxes[0].Id);
        Assert.True(document.CopyCommand.CanExecute(null));
        Assert.True(changes > 0);

        document.CopyCommand.Execute(null);
        Assert.True(document.PasteCommand.CanExecute(null));
        document.PasteCommand.Execute(null);
        Assert.Equal(4, document.Layout.VisualEntries.Count());
    }
}
