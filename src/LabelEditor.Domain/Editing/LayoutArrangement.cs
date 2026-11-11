using LabelEditor.Domain.Entries;
using LabelEditor.Domain.Geometry;
using LabelEditor.Domain.Layout;

namespace LabelEditor.Domain.Editing;

/// <summary>Alignment commands of the LEdit layout toolbar.</summary>
public enum Alignment
{
    Left,
    Right,
    Top,
    Bottom,
    CenterHorizontallyInLabel,
    CenterVerticallyInLabel,
}

/// <summary>Size-matching commands.</summary>
public enum SizeMatch
{
    Width,
    Height,
    Both,
}

/// <summary>Even-spacing commands.</summary>
public enum Distribution
{
    Horizontal,
    Vertical,
}

/// <summary>
/// Arrangement operations on a selection. The first id in a selection is the dominant entry
/// that others align or size to, matching LEdit behaviour.
/// </summary>
public static class LayoutArrangement
{
    public static LabelLayout Align(LabelLayout layout, IReadOnlyList<EntryId> selection, Alignment alignment)
    {
        var entries = Resolve(layout, selection);
        if (entries.Count == 0)
        {
            return layout;
        }

        var label = layout.Header.Shape.Bounds;
        switch (alignment)
        {
            case Alignment.CenterHorizontallyInLabel:
            {
                var group = Group(entries);
                return Move(layout, entries, _ => (label.CenterX - group.CenterX, 0));
            }

            case Alignment.CenterVerticallyInLabel:
            {
                var group = Group(entries);
                return Move(layout, entries, _ => (0, label.CenterY - group.CenterY));
            }
        }

        if (entries.Count < 2)
        {
            return layout;
        }

        var dominant = entries[0].Bounds;
        return Move(layout, entries.Skip(1), e => alignment switch
        {
            Alignment.Left => (dominant.Left - e.Bounds.Left, 0),
            Alignment.Right => (dominant.Right - e.Bounds.Right, 0),
            Alignment.Top => (0, dominant.Top - e.Bounds.Top),
            Alignment.Bottom => (0, dominant.Bottom - e.Bounds.Bottom),
            _ => (0, 0),
        });
    }

    public static LabelLayout MatchSize(LabelLayout layout, IReadOnlyList<EntryId> selection, SizeMatch match)
    {
        var entries = Resolve(layout, selection);
        if (entries.Count < 2)
        {
            return layout;
        }

        // Width and height are taken along / across each entry's text, so rotated entries get the length of the
        // reference text along their own reading direction instead of being stretched the wrong way.
        var (along, across) = EntryFrame.LocalSize(entries[0]);
        var ids = entries.Skip(1).Where(e => !e.IsSizeLocked).Select(e => e.Id).ToHashSet();
        var matchWidth = match is SizeMatch.Width or SizeMatch.Both;
        var matchHeight = match is SizeMatch.Height or SizeMatch.Both;
        return layout.UpdateEntries(ids, e =>
        {
            if (e.IsSegment)
            {
                // Lines keep their direction (signed sizes) and have no text frame.
                var dominant = entries[0].Bounds.Normalize();
                var b = e.Bounds;
                return e with
                {
                    Bounds = b with
                    {
                        Width = matchWidth ? WithSign(dominant.Width, b.Width) : b.Width,
                        Height = matchHeight ? WithSign(dominant.Height, b.Height) : b.Height,
                    },
                };
            }

            var (ownAlong, ownAcross) = EntryFrame.LocalSize(e);
            return e with { Bounds = EntryFrame.WithLocalSize(e, matchWidth ? along : ownAlong, matchHeight ? across : ownAcross) };
        });
    }

    /// <summary>Spaces entries so the gaps between them are equal, keeping the outermost two in place.</summary>
    public static LabelLayout Distribute(LabelLayout layout, IReadOnlyList<EntryId> selection, Distribution direction)
    {
        var entries = Resolve(layout, selection);
        if (entries.Count < 3)
        {
            return layout;
        }

        var horizontal = direction == Distribution.Horizontal;
        var ordered = entries.OrderBy(e => horizontal ? e.Bounds.Left : e.Bounds.Top).ToList();
        int Start(LabelEntry e) => horizontal ? e.Bounds.Left : e.Bounds.Top;
        int Size(LabelEntry e) => horizontal ? e.Bounds.Right - e.Bounds.Left : e.Bounds.Bottom - e.Bounds.Top;

        var first = ordered[0];
        var last = ordered[^1];
        var span = Start(last) + Size(last) - Start(first);
        var occupied = ordered.Sum(Size);
        var gap = (span - occupied) / (double)(ordered.Count - 1);

        var deltas = new Dictionary<EntryId, int>();
        double cursor = Start(first);
        foreach (var e in ordered)
        {
            deltas[e.Id] = (int)Math.Round(cursor) - Start(e);
            cursor += Size(e) + gap;
        }

        return Move(layout, ordered, e => horizontal ? (deltas[e.Id], 0) : (0, deltas[e.Id]));
    }

    /// <summary>Snaps the top-left corner of each entry to the grid.</summary>
    public static LabelLayout SnapToGrid(LabelLayout layout, IReadOnlyList<EntryId> selection, int grid)
    {
        var entries = Resolve(layout, selection);
        return Move(layout, entries, e => (LayoutUnits.Snap(e.Bounds.Left, grid) - e.Bounds.Left, LayoutUnits.Snap(e.Bounds.Top, grid) - e.Bounds.Top));
    }

    private static int WithSign(int magnitude, int original) => original < 0 ? -magnitude : magnitude;

    private static List<LabelEntry> Resolve(LabelLayout layout, IReadOnlyList<EntryId> selection) =>
        selection.Select(layout.Find).OfType<LabelEntry>().Where(e => e.IsVisual).ToList();

    private static LabelRect Group(IEnumerable<LabelEntry> entries) =>
        entries.Select(e => e.Bounds.Normalize()).Aggregate((a, b) => a.Union(b));

    private static LabelLayout Move(LabelLayout layout, IEnumerable<LabelEntry> entries, Func<LabelEntry, (int Dx, int Dy)> delta)
    {
        var result = layout;
        foreach (var entry in entries.Where(e => !e.IsPositionLocked))
        {
            var (dx, dy) = delta(entry);
            if (dx != 0 || dy != 0)
            {
                result = result.ReplaceEntry(entry with { Bounds = entry.Bounds.Offset(dx, dy) });
            }
        }

        return result;
    }
}
