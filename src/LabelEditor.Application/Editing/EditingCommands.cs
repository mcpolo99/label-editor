using LabelEditor.Application.Documents;
using LabelEditor.Domain.Editing;
using LabelEditor.Domain.Entries;
using LabelEditor.Domain.Geometry;
using LabelEditor.Domain.Layout;

namespace LabelEditor.Application.Editing;

/// <summary>
/// The editor's commands, each one undoable step on a session. Kept as extension methods so the
/// session stays a small, focused state holder and commands are easy to find and test.
/// </summary>
public static class EditingCommands
{
    public static bool AddEntry(this EditorSession session, LabelEntry entry) =>
        session.Execute($"Add {entry.Kind}", l => l.AddEntry(entry), [entry.Id]);

    public static bool DeleteSelection(this EditorSession session)
    {
        var ids = session.Selection.AsSet();
        return ids.Count > 0 && session.Execute(ids.Count == 1 ? "Delete entry" : $"Delete {ids.Count} entries", l => l.RemoveEntries(ids), []);
    }

    public static bool DuplicateSelection(this EditorSession session, int offset)
    {
        var copies = SelectedEntries(session).Select(e => e.CloneWithNewId() with { Bounds = e.Bounds.Offset(offset, offset) }).ToList();
        return copies.Count > 0 && session.Execute("Duplicate", l => l.AddEntries(copies), copies.Select(c => c.Id));
    }

    public static void CopySelection(this EditorSession session, EntryClipboard clipboard)
    {
        var entries = SelectedEntries(session);
        if (entries.Count > 0)
        {
            clipboard.Set(entries);
        }
    }

    public static bool CutSelection(this EditorSession session, EntryClipboard clipboard)
    {
        session.CopySelection(clipboard);
        return session.DeleteSelection();
    }

    public static bool Paste(this EditorSession session, EntryClipboard clipboard, int offsetStep)
    {
        if (!clipboard.HasContent)
        {
            return false;
        }

        var entries = clipboard.TakeForPaste(offsetStep);
        return session.Execute("Paste", l => l.AddEntries(entries), entries.Select(e => e.Id));
    }

    public static bool MoveSelection(this EditorSession session, int dx, int dy)
    {
        var ids = session.Selection.AsSet();
        return session.Execute("Move", l => l.MoveEntries(ids, dx, dy));
    }

    public static bool Align(this EditorSession session, Alignment alignment) =>
        session.Execute($"Align {alignment}", l => LayoutArrangement.Align(l, session.Selection.Ids, alignment));

    public static bool MatchSize(this EditorSession session, SizeMatch match) =>
        session.Execute($"Make same {match}", l => LayoutArrangement.MatchSize(l, session.Selection.Ids, match));

    public static bool Distribute(this EditorSession session, Distribution distribution) =>
        session.Execute($"Space {distribution}", l => LayoutArrangement.Distribute(l, session.Selection.Ids, distribution));

    public static bool SnapSelectionToGrid(this EditorSession session) =>
        session.Execute("Snap to grid", l => LayoutArrangement.SnapToGrid(l, session.Selection.Ids, l.Header.GridSize));

    public static bool BringToFront(this EditorSession session)
    {
        var ids = session.Selection.AsSet();
        return session.Execute("Bring to front", l => l.BringToFront(ids));
    }

    public static bool SendToBack(this EditorSession session)
    {
        var ids = session.Selection.AsSet();
        return session.Execute("Send to back", l => l.SendToBack(ids));
    }

    public static void SelectAll(this EditorSession session) =>
        session.Selection.Select(session.Layout.VisualEntries.Select(e => e.Id));

    public static bool SetLabelSize(this EditorSession session, int width, int height) =>
        session.Execute("Label size", l => l.WithLabelSize(width, height));

    public static bool SetGrid(this EditorSession session, int gridSize) =>
        session.Execute("Grid", l => l.WithHeader(h => h with { GridSize = Math.Max(0, gridSize) }));

    public static bool SetUnit(this EditorSession session, MeasurementUnit unit) =>
        session.Execute("Measurement unit", l => l.WithHeader(h => h with { Unit = unit }));

    /// <summary>Replaces one entry (property edits). The entry keeps its id.</summary>
    public static bool UpdateEntry(this EditorSession session, LabelEntry updated, string description = "Edit properties") =>
        session.Execute(description, l => l.Find(updated.Id) is null ? l : l.ReplaceEntry(updated));

    /// <summary>Updates all selected entries of type <typeparamref name="T"/>.</summary>
    public static bool UpdateSelected<T>(this EditorSession session, string description, Func<T, T> update)
        where T : LabelEntry
    {
        var ids = session.Selection.AsSet();
        return session.Execute(description, l => l.UpdateEntries(ids, e => e is T typed ? update(typed) : e));
    }

    public static IReadOnlyList<LabelEntry> SelectedEntries(this EditorSession session) =>
        session.Selection.Ids.Select(session.Layout.Find).OfType<LabelEntry>().ToList();

    /// <summary>Starts a drag that moves the selection; call <c>Update</c> with the total offset so far.</summary>
    public static SelectionDrag BeginMove(this EditorSession session) => new(session.BeginInteractive("Move"), session.Selection.AsSet());

    /// <summary>Starts a resize of one entry by one of its handles.</summary>
    public static EntryResize BeginResize(this EditorSession session, EntryId id, ResizeHandle handle) =>
        new(session.BeginInteractive("Resize"), id, handle);
}

/// <summary>Interactive move of a set of entries.</summary>
public sealed class SelectionDrag(InteractiveEdit edit, IReadOnlySet<EntryId> ids) : IDisposable
{
    public void Update(int dx, int dy) => edit.Update(l => l.MoveEntries(ids, dx, dy));

    public void Commit() => edit.Commit();

    public void Dispose() => edit.Dispose();
}

/// <summary>Interactive resize of one entry. Lines move their nearest end instead of a box edge.</summary>
public sealed class EntryResize(InteractiveEdit edit, EntryId id, ResizeHandle handle) : IDisposable
{
    public void Update(int dx, int dy, int minimumSize = 0) => edit.Update(l => Apply(l, dx, dy, minimumSize));

    public void Commit() => edit.Commit();

    public void Dispose() => edit.Dispose();

    private LabelLayout Apply(LabelLayout layout, int dx, int dy, int minimumSize)
    {
        if (layout.Find(id) is not { } entry)
        {
            return layout;
        }

        var bounds = entry.IsSegment ? MoveSegmentEnd(entry.Bounds, dx, dy) : entry.Bounds.Resize(handle, dx, dy, minimumSize);
        return layout.ReplaceEntry(entry with { Bounds = bounds });
    }

    // For segments only two handles are meaningful: TopLeft = start point, BottomRight = end point.
    private LabelRect MoveSegmentEnd(LabelRect b, int dx, int dy) => handle == ResizeHandle.TopLeft
        ? new LabelRect(b.X + dx, b.Y + dy, b.Width - dx, b.Height - dy)
        : b with { Width = b.Width + dx, Height = b.Height + dy };
}
