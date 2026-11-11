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

    /// <summary>Visual entries that intersect <paramref name="area"/>, in z-order.</summary>
    public static IReadOnlyList<LabelEntry> EntriesIn(LabelLayout layout, LabelRect area) =>
        layout.VisualEntries.Where(e => area.IntersectsWith(e.Bounds)).ToList();

    public static bool Hits(LabelEntry entry, LabelPoint point, int tolerance) =>
        entry.IsSegment ? entry.Bounds.DistanceToSegment(point) <= tolerance : entry.Bounds.Contains(point, tolerance);
}
