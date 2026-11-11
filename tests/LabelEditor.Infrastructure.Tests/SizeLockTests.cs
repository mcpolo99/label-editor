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
    public async Task No_sidecar_is_written_when_nothing_is_locked_and_a_stale_one_is_removed()
    {
        var path = await CopyFixtureAsync("Plats128C.xml");
        await _store.SaveAsync(LockText(await _store.LoadAsync(path), out _), path);

        await _store.SaveAsync(await new FileLabelDocumentStore(new LeditLabelFormat()).LoadAsync(path) is var l
            ? l with { Entries = [.. l.Entries.Select(e => e with { IsSizeLocked = false })] }
            : l, path);

        Assert.False(File.Exists(EditorMetadataSidecar.PathFor(path)));
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
