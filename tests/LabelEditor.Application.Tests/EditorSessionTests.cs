using LabelEditor.Application.Documents;
using LabelEditor.Application.Editing;
using LabelEditor.Domain.Entries;
using LabelEditor.Domain.Geometry;
using LabelEditor.Domain.Layout;

namespace LabelEditor.Application.Tests;

public class EditorSessionTests
{
    private static EditorSession NewSession() => new(LabelLayout.CreateNew());

    private static LabelEntry AddBox(EditorSession session, int x = 0, int y = 0)
    {
        var entry = EntryFactory.Create(EditorTool.Ellipse, new LabelRect(x, y, 5_000, 5_000));
        session.AddEntry(entry);
        return entry;
    }

    [Fact]
    public void Edits_are_undoable_and_redoable()
    {
        var session = NewSession();
        var original = session.Layout;
        var box = AddBox(session);

        Assert.True(session.IsDirty);
        Assert.True(session.Undo());
        Assert.Same(original, session.Layout);
        Assert.False(session.IsDirty);
        Assert.True(session.Redo());
        Assert.NotNull(session.Layout.Find(box.Id));
    }

    [Fact]
    public void No_op_edits_are_not_recorded()
    {
        var session = NewSession();

        Assert.False(session.Execute("nothing", l => l));
        Assert.False(session.History.CanUndo);
    }

    [Fact]
    public void Adding_selects_the_new_entry_and_undo_prunes_the_selection()
    {
        var session = NewSession();
        var box = AddBox(session);

        Assert.Equal([box.Id], session.Selection.Ids);
        session.Undo();
        Assert.True(session.Selection.IsEmpty);
    }

    [Fact]
    public void A_drag_is_one_undo_step()
    {
        var session = NewSession();
        var box = AddBox(session);

        using (var drag = session.BeginMove())
        {
            drag.Update(1_000, 0);
            drag.Update(2_000, 500);
            drag.Update(3_000, 1_000);
            drag.Commit();
        }

        Assert.Equal(new LabelPoint(3_000, 1_000), session.Layout.Find(box.Id)!.Bounds.Start);
        session.Undo();
        Assert.Equal(LabelPoint.Zero, session.Layout.Find(box.Id)!.Bounds.Start);
    }

    [Fact]
    public void A_cancelled_drag_restores_the_layout_and_records_nothing()
    {
        var session = NewSession();
        AddBox(session);
        var before = session.Layout;

        using (var drag = session.BeginMove())
        {
            drag.Update(5_000, 5_000);
        }

        Assert.Same(before, session.Layout);
        Assert.Equal("Add Ellipse", session.History.UndoDescription);
    }

    [Fact]
    public void Edits_during_a_drag_are_rejected()
    {
        var session = NewSession();
        using var drag = session.BeginMove();

        Assert.Throws<InvalidOperationException>(() => session.Undo());
    }

    [Fact]
    public void Line_resize_moves_the_grabbed_end_point()
    {
        var session = NewSession();
        var line = EntryFactory.Create(EditorTool.Line, new LabelRect(0, 0, 10_000, 0));
        session.AddEntry(line);

        using (var resize = session.BeginResize(line.Id, ResizeHandle.TopLeft))
        {
            resize.Update(2_000, 3_000);
            resize.Commit();
        }

        var bounds = session.Layout.Find(line.Id)!.Bounds;
        Assert.Equal(new LabelPoint(2_000, 3_000), bounds.Start);
        Assert.Equal(new LabelPoint(10_000, 0), bounds.End);
    }

    [Fact]
    public void Paste_offsets_each_copy_and_creates_new_ids()
    {
        var session = NewSession();
        var clipboard = new EntryClipboard();
        var box = AddBox(session, 1_000, 1_000);
        session.CopySelection(clipboard);

        session.Paste(clipboard, 1_000);
        var first = session.Selection.Ids.Single();
        session.Paste(clipboard, 1_000);
        var second = session.Selection.Ids.Single();

        Assert.NotEqual(box.Id, first);
        Assert.Equal(new LabelPoint(2_000, 2_000), session.Layout.Find(first)!.Bounds.Start);
        Assert.Equal(new LabelPoint(3_000, 3_000), session.Layout.Find(second)!.Bounds.Start);
    }

    [Fact]
    public void Delete_selection_removes_entries_but_never_the_data_record()
    {
        var session = NewSession();
        AddBox(session);
        session.SelectAll();

        session.DeleteSelection();

        Assert.IsType<DataRecordEntry>(Assert.Single(session.Layout.Entries));
    }

    [Fact]
    public void Changes_are_reported_with_what_changed()
    {
        var session = NewSession();
        var changes = new List<SessionChange>();
        session.Changed += (_, e) => changes.Add(e.Change);

        AddBox(session);

        Assert.Contains(changes, c => c.HasFlag(SessionChange.Layout) && c.HasFlag(SessionChange.History));
        Assert.Contains(changes, c => c.HasFlag(SessionChange.Selection));
    }

    [Fact]
    public void History_capacity_drops_the_oldest_steps()
    {
        var session = new EditorSession(LabelLayout.CreateNew(), historyCapacity: 2);
        AddBox(session);
        AddBox(session);
        AddBox(session);

        Assert.True(session.Undo());
        Assert.True(session.Undo());
        Assert.False(session.Undo());
    }

    [Fact]
    public void Consecutive_edits_with_the_same_merge_key_are_one_undo_step()
    {
        var session = NewSession();
        var box = AddBox(session);
        var start = session.Layout;

        foreach (var x in new[] { 1, 12, 123 })
        {
            session.Execute("X", l => l.ReplaceEntry(l.Find(box.Id)! with { Bounds = box.Bounds with { X = x } }), mergeKey: "x");
        }

        session.Undo();
        Assert.Same(start, session.Layout);
    }

    [Fact]
    public void Selection_keeps_the_first_entry_as_dominant()
    {
        var session = NewSession();
        var a = AddBox(session);
        var b = AddBox(session);

        session.Selection.Select(a.Id);
        session.Selection.Select(b.Id, SelectionMode.Add);
        Assert.Equal(a.Id, session.Selection.Dominant);

        session.Selection.Select(a.Id, SelectionMode.Toggle);
        Assert.Equal(b.Id, session.Selection.Dominant);
    }
}
