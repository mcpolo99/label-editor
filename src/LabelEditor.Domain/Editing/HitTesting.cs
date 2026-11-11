using LabelEditor.Domain.Entries;
using LabelEditor.Domain.Geometry;
using LabelEditor.Domain.Layout;

namespace LabelEditor.Domain.Editing;

/// <summary>Finds entries under a point or inside a marquee.</summary>
public static class HitTesting
{
    /// <summary>The topmost visual entry at <paramref name="point"/>, or null.</summary>
    /// <param name="layout">Layout to search.</param>
    /// <param name="point">Probe point in layout units.</param>
    /// <param name="tolerance">Grab tolerance in layout units (thin lines need some).</param>
    public static LabelEntry? EntryAt(LabelLayout layout, LabelPoint point, int tolerance)
    {
        for (var i = layout.Entries.Count - 1; i >= 0; i--)
        {
            var entry = layout.Entries[i];
            if (entry.IsVisual && Hits(entry, point, tolerance))
            {
                return entry;
            }
        }

        return null;
    }

    /// <summary>All visual entries at <paramref name="point"/>, topmost first (for picking entries hidden behind others).</summary>
    public static IReadOnlyList<LabelEntry> EntriesAt(LabelLayout layout, LabelPoint point, int tolerance)
    {
        var result = new List<LabelEntry>();
        for (var i = layout.Entries.Count - 1; i >= 0; i--)
        {
            var entry = layout.Entries[i];
            if (entry.IsVisual && Hits(entry, point, tolerance))
            {
                result.Add(entry);
            }
        }

        return result;
    }

    /// <summary>
    /// "Select behind": the entry after <paramref name="current"/> in the stack at <paramref name="point"/> (wrapping),
    /// or the topmost one when <paramref name="current"/> is not in that stack.
    /// </summary>
    public static LabelEntry? NextEntryAt(LabelLayout layout, LabelPoint point, int tolerance, EntryId? current)
    {
        var stack = EntriesAt(layout, point, tolerance);
        if (stack.Count == 0)
        {
            return null;
        }

        var index = current is { } id ? stack.ToList().FindIndex(e => e.Id == id) : -1;
        return stack[(index + 1) % stack.Count];
    }

    /// <summary>Visual entries that intersect <paramref name="area"/>, in z-order.</summary>
    public static IReadOnlyList<LabelEntry> EntriesIn(LabelLayout layout, LabelRect area) =>
        layout.VisualEntries.Where(e => area.IntersectsWith(e.Bounds)).ToList();

    public static bool Hits(LabelEntry entry, LabelPoint point, int tolerance) =>
        entry.IsSegment ? entry.Bounds.DistanceToSegment(point) <= tolerance : entry.Bounds.Contains(point, tolerance);
}
