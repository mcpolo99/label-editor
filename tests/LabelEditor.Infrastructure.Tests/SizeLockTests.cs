using LabelEditor.Application.Documents;
using LabelEditor.Application.Editing;
using LabelEditor.Domain.Editing;
using LabelEditor.Domain.Entries;
using LabelEditor.Domain.Geometry;
using LabelEditor.Domain.Layout;
using LabelEditor.Infrastructure.Ledit;
using LabelEditor.Infrastructure.Storage;

namespace LabelEditor.Infrastructure.Tests;

public sealed class SizeLockTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "LabelEditorTests", Guid.NewGuid().ToString("N"));
    private readonly FileLabelDocumentStore _store = new(new LeditLabelFormat());

    public SizeLockTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private async Task<string> CopyFixtureAsync(string name)
    {
        var path = Path.Combine(_folder, name);
        await File.WriteAllBytesAsync(path, await File.ReadAllBytesAsync(TestData.Label(name)));
        return path;
    }

    private static LabelLayout LockText(LabelLayout layout, out EntryId id)
    {
        var text = layout.Entries.OfType<TextEntry>().First();
        id = text.Id;
        return layout.ReplaceEntry(text with { IsSizeLocked = true });
    }

    [Fact]
    public async Task Size_locks_survive_save_and_load_without_touching_the_ledit_file()
    {
        var path = await CopyFixtureAsync("Plats128C.xml");
        var original = await File.ReadAllBytesAsync(path);
        var locked = LockText(await _store.LoadAsync(path), out _);

        await _store.SaveAsync(locked, path);

        Assert.Equal(original, await File.ReadAllBytesAsync(path)); // LEdit output byte-for-byte unchanged
        Assert.True(File.Exists(EditorMetadataSidecar.PathFor(path)));
        var reloaded = await _store.LoadAsync(path);
        Assert.True(reloaded.Entries.OfType<TextEntry>().First().IsSizeLocked);
        Assert.Single(reloaded.Entries, e => e.IsSizeLocked);
    }

    [Fact]
    public async Task Unlocking_removes_the_lock_but_keeps_the_history()
    {
        var path = await CopyFixtureAsync("Plats128C.xml");
        await _store.SaveAsync(LockText(await _store.LoadAsync(path), out _), path);

        await _store.SaveAsync(await new FileLabelDocumentStore(new LeditLabelFormat()).LoadAsync(path) is var l
            ? l with { Entries = [.. l.Entries.Select(e => e with { IsSizeLocked = false })] }
            : l, path);

        Assert.DoesNotContain((await _store.LoadAsync(path)).Entries, e => e.IsSizeLocked);
        Assert.NotEmpty(await _store.GetVersionsAsync(path));
    }

    [Fact]
    public async Task A_lock_is_dropped_rather_than_moved_to_the_wrong_entry_when_the_label_changed_elsewhere()
    {
        var path = await CopyFixtureAsync("Plats128C.xml");
        var layout = LockText(await _store.LoadAsync(path), out var id);
        await _store.SaveAsync(layout, path);

        // Someone resizes that entry in LEdit (the sidecar is not updated).
        var edited = layout.ReplaceEntry(layout.Find(id)! with { Bounds = layout.Find(id)!.Bounds with { Width = 12_345 }, IsSizeLocked = false });
        await using (var stream = File.Create(path))
        {
            await new LeditLabelFormat().WriteAsync(edited, stream);
        }

        Assert.DoesNotContain((await _store.LoadAsync(path)).Entries, e => e.IsSizeLocked);
    }

    [Fact]
    public async Task A_corrupt_sidecar_never_prevents_opening_the_label()
    {
        var path = await CopyFixtureAsync("Plats128C.xml");
        await File.WriteAllTextAsync(EditorMetadataSidecar.PathFor(path), "{ broken");

        var layout = await _store.LoadAsync(path);

        Assert.NotEmpty(layout.Entries);
    }

    [Fact]
    public void Locked_entries_move_but_do_not_resize_or_match_size()
    {
        var a = new EllipseEntry { Id = EntryId.New(), Bounds = new LabelRect(0, 0, 10_000, 10_000) };
        var b = new EllipseEntry { Id = EntryId.New(), Bounds = new LabelRect(20_000, 0, 4_000, 4_000), IsSizeLocked = true };
        var session = new EditorSession(LabelLayout.CreateNew().AddEntries([a, b]));

        using (var resize = session.BeginResize(b.Id, ResizeHandle.BottomRight))
        {
            resize.Update(5_000, 5_000);
            resize.Commit();
        }

        session.Selection.Select([a.Id, b.Id]);
        session.MatchSize(SizeMatch.Both);
        session.Selection.Select(b.Id);
        session.MoveSelection(1_000, 2_000);

        Assert.Equal(new LabelRect(21_000, 2_000, 4_000, 4_000), session.Layout.Find(b.Id)!.Bounds);
    }

    [Fact]
    public async Task Position_locks_survive_save_and_load_and_old_sidecars_still_read()
    {
        var path = await CopyFixtureAsync("Plats128C.xml");
        var layout = await _store.LoadAsync(path);
        var text = layout.Entries.OfType<TextEntry>().First();
        await _store.SaveAsync(layout.ReplaceEntry(text with { IsPositionLocked = true }), path);

        var reloaded = (await _store.LoadAsync(path)).Entries.OfType<TextEntry>().First();
        Assert.True(reloaded.IsPositionLocked);
        Assert.False(reloaded.IsSizeLocked);

        // A sidecar written before position locks existed has no PositionLocked value.
        var sidecar = EditorMetadataSidecar.PathFor(path);
        var json = (await File.ReadAllTextAsync(sidecar)).Replace("\"PositionLocked\": true", "\"PositionLocked\": false").Replace("\"SizeLocked\": false", "\"SizeLocked\": true");
        json = System.Text.RegularExpressions.Regex.Replace(json, @",\s*""PositionLocked"":\s*false", string.Empty);
        await File.WriteAllTextAsync(sidecar, json);
        var old = (await _store.LoadAsync(path)).Entries.OfType<TextEntry>().First();
        Assert.True(old.IsSizeLocked);
        Assert.False(old.IsPositionLocked);
    }

    [Fact]
    public void Dragging_a_handle_with_several_entries_selected_resizes_them_all_along_their_own_text()
    {
        var plain = new TextEntry { Id = EntryId.New(), Bounds = new LabelRect(0, 0, 40_000, 8_000) };
        var turned = new TextEntry { Id = EntryId.New(), Bounds = new LabelRect(50_000, 20_000, 10_000, 30_000) };
        turned = turned with { Font = turned.Font.WithRotation(Domain.Text.TextRotation.Ccw90) };
        var locked = new EllipseEntry { Id = EntryId.New(), Bounds = new LabelRect(0, 30_000, 5_000, 5_000), IsSizeLocked = true };
        var session = new EditorSession(LabelLayout.CreateNew().AddEntries([plain, turned, locked]));
        session.Selection.Select([plain.Id, turned.Id, locked.Id]);

        using (var resize = session.BeginResize(plain.Id, ResizeHandle.Right))
        {
            resize.Update(5_000, 0);
            resize.Commit();
        }

        Assert.Equal(new LabelRect(0, 0, 45_000, 8_000), session.Layout.Find(plain.Id)!.Bounds);
        Assert.Equal(new LabelRect(50_000, 15_000, 10_000, 35_000), session.Layout.Find(turned.Id)!.Bounds); // longer, not wider
        Assert.Equal(locked.Bounds, session.Layout.Find(locked.Id)!.Bounds);
        Assert.Equal("Resize 3 entries", session.History.UndoDescription);
        session.Undo();
        Assert.Equal(turned.Bounds, session.Layout.Find(turned.Id)!.Bounds);
    }

    [Fact]
    public void Lock_position_is_one_undoable_command_for_the_selection()
    {
        var a = new EllipseEntry { Id = EntryId.New(), Bounds = new LabelRect(0, 0, 10_000, 10_000) };
        var session = new EditorSession(LabelLayout.CreateNew().AddEntry(a));
        session.Selection.Select(a.Id);

        Assert.True(session.SetPositionLocked(true));
        session.MoveSelection(1_000, 0);
        Assert.Equal(a.Bounds, session.Layout.Find(a.Id)!.Bounds);
        session.Undo();
        Assert.False(session.Layout.Find(a.Id)!.IsPositionLocked);
    }

    [Fact]
    public void Lock_size_is_one_undoable_command_for_the_selection()
    {
        var a = new EllipseEntry { Id = EntryId.New(), Bounds = new LabelRect(0, 0, 10_000, 10_000) };
        var session = new EditorSession(LabelLayout.CreateNew().AddEntry(a));
        session.Selection.Select(a.Id);

        Assert.True(session.SetSizeLocked(true));
        Assert.True(session.Layout.Find(a.Id)!.IsSizeLocked);
        Assert.True(session.IsDirty);
        session.Undo();
        Assert.False(session.Layout.Find(a.Id)!.IsSizeLocked);
    }
}
