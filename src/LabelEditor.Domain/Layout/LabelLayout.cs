using LabelEditor.Domain.Common;
using LabelEditor.Domain.Entries;
using LabelEditor.Domain.Geometry;

namespace LabelEditor.Domain.Layout;

/// <summary>
/// The aggregate root: a label layout with its header and an ordered list of entries.
/// Entry order is the drawing order (last = topmost) and is preserved on save.
/// </summary>
public sealed record LabelLayout
{
    public LabelHeader Header { get; init; } = new();

    public ValueList<LabelEntry> Entries { get; init; } = [];

    /// <summary>Entries drawn on the canvas, in z-order.</summary>
    public IEnumerable<LabelEntry> VisualEntries => Entries.Where(e => e.IsVisual);

    /// <summary>An empty 100 × 60 mm label with the data record LEdit always writes.</summary>
    public static LabelLayout CreateNew(int width = 100_000, int height = 60_000) => new()
    {
        Header = new LabelHeader { Shape = new LabelShape { Width = width, Height = height } },
        Entries = [new DataRecordEntry { Id = EntryId.New(), Bounds = default }],
    };

    public LabelEntry? Find(EntryId id)
    {
        foreach (var entry in Entries)
        {
            if (entry.Id == id)
            {
                return entry;
            }
        }

        return null;
    }

    public int IndexOf(EntryId id)
    {
        for (var i = 0; i < Entries.Count; i++)
        {
            if (Entries[i].Id == id)
            {
                return i;
            }
        }

        return -1;
    }

    public LabelLayout AddEntry(LabelEntry entry) => this with { Entries = Entries.Add(entry) };

    public LabelLayout AddEntries(IEnumerable<LabelEntry> entries) => this with { Entries = Entries.AddRange(entries) };

    public LabelLayout RemoveEntries(IReadOnlySet<EntryId> ids) => this with { Entries = Entries.RemoveAll(e => ids.Contains(e.Id)) };

    /// <summary>Replaces the entry with the same id. Throws if it does not exist.</summary>
    public LabelLayout ReplaceEntry(LabelEntry entry)
    {
        var index = IndexOf(entry.Id);
        if (index < 0)
        {
            throw new KeyNotFoundException($"Entry {entry.Id} is not part of the layout.");
        }

        return this with { Entries = Entries.SetItem(index, entry) };
    }

    /// <summary>Applies <paramref name="update"/> to every entry whose id is in <paramref name="ids"/>.</summary>
    public LabelLayout UpdateEntries(IReadOnlySet<EntryId> ids, Func<LabelEntry, LabelEntry> update)
    {
        if (ids.Count == 0)
        {
            return this;
        }

        return this with { Entries = Entries.Select(e => ids.Contains(e.Id) ? update(e) : e).ToValueList() };
    }

    public LabelLayout MoveEntries(IReadOnlySet<EntryId> ids, int dx, int dy) =>
        dx == 0 && dy == 0 ? this : UpdateEntries(ids, e => e with { Bounds = e.Bounds.Offset(dx, dy) });

    public LabelLayout WithHeader(Func<LabelHeader, LabelHeader> update) => this with { Header = update(Header) };

    public LabelLayout WithLabelSize(int width, int height) =>
        WithHeader(h => h with { Shape = h.Shape with { Width = Math.Max(1, width), Height = Math.Max(1, height) } });

    /// <summary>Moves the given entries to the end of the list (drawn on top), keeping their relative order.</summary>
    public LabelLayout BringToFront(IReadOnlySet<EntryId> ids) =>
        this with { Entries = Entries.Where(e => !ids.Contains(e.Id)).Concat(Entries.Where(e => ids.Contains(e.Id))).ToValueList() };

    /// <summary>Moves the given entries to the start of the visual list (drawn below everything).</summary>
    public LabelLayout SendToBack(IReadOnlySet<EntryId> ids) =>
        this with { Entries = Entries.Where(e => !e.IsVisual).Concat(Entries.Where(e => e.IsVisual && ids.Contains(e.Id))).Concat(Entries.Where(e => e.IsVisual && !ids.Contains(e.Id))).ToValueList() };

    /// <summary>Bounding box of the given entries, or null when none match.</summary>
    public LabelRect? BoundsOf(IEnumerable<EntryId> ids)
    {
        LabelRect? result = null;
        foreach (var id in ids)
        {
            if (Find(id) is { } entry)
            {
                result = result is { } r ? r.Union(entry.Bounds) : entry.Bounds.Normalize();
            }
        }

        return result;
    }
}
