using LabelEditor.Domain.Geometry;
using LabelEditor.Domain.Layout;

namespace LabelEditor.Domain.Editing;

/// <summary>
/// A guide line shown while snapping: a vertical line at x = <see cref="Position"/> from y = <see cref="From"/>
/// to <see cref="To"/>, or a horizontal one at y = Position from x = From to To. Layout units.
/// </summary>
public readonly record struct SnapLine(bool IsVertical, int Position, int From, int To);

/// <summary>How far to shift what is being dragged so it lines up, and the guides to show.</summary>
/// <param name="Dx">Horizontal correction; 0 when nothing lined up.</param>
/// <param name="Dy">Vertical correction.</param>
/// <param name="SnappedX">True when a vertical guide was found (the grid should not move X any more).</param>
/// <param name="SnappedY">True when a horizontal guide was found.</param>
/// <param name="Lines">Guide lines through the aligned edges.</param>
public sealed record SnapResult(int Dx, int Dy, bool SnappedX, bool SnappedY, IReadOnlyList<SnapLine> Lines)
{
    public static SnapResult None { get; } = new(0, 0, false, false, []);
}

/// <summary>
/// Snap-to-entries: edges and centres of the dragged box line up with the edges and centres of the other
/// entries and of the label itself, when they come within a tolerance.
/// </summary>
public static class SnapGuides
{
    /// <summary>The boxes to align with: every visual entry not being dragged, plus the label.</summary>
    public static IReadOnlyList<LabelRect> TargetsFor(LabelLayout layout, IReadOnlyCollection<Entries.EntryId> dragged) =>
        [layout.Header.Shape.Bounds, .. layout.VisualEntries.Where(e => !dragged.Contains(e.Id)).Select(e => e.Bounds.Normalize())];

    /// <summary>Moving a box: its left/centre/right and top/centre/bottom may each line up.</summary>
    public static SnapResult ForMove(LabelRect moving, IReadOnlyList<LabelRect> targets, int tolerance)
    {
        moving = moving.Normalize();
        return Snap(moving, [moving.Left, moving.CenterX, moving.Right], [moving.Top, moving.CenterY, moving.Bottom], targets, tolerance);
    }

    /// <summary>
    /// Resizing: only the dragged point moves (a corner or edge handle, or a line end). <paramref name="movesX"/> /
    /// <paramref name="movesY"/> say which coordinates of it the handle changes.
    /// </summary>
    public static SnapResult ForPoint(LabelPoint point, bool movesX, bool movesY, IReadOnlyList<LabelRect> targets, int tolerance) =>
        Snap(new LabelRect(point.X, point.Y, 0, 0), movesX ? [point.X] : [], movesY ? [point.Y] : [], targets, tolerance);

    private static SnapResult Snap(LabelRect moving, int[] xs, int[] ys, IReadOnlyList<LabelRect> targets, int tolerance)
    {
        var dx = Best(xs, targets.SelectMany(t => new[] { t.Left, t.CenterX, t.Right }), tolerance);
        var dy = Best(ys, targets.SelectMany(t => new[] { t.Top, t.CenterY, t.Bottom }), tolerance);
        if (dx is null && dy is null)
        {
            return SnapResult.None;
        }

        var snapped = moving.Offset(dx ?? 0, dy ?? 0);
        var lines = new List<SnapLine>();
        if (dx is not null)
        {
            foreach (var x in xs.Select(x => x + dx.Value).Distinct())
            {
                var aligned = targets.Where(t => t.Left == x || t.CenterX == x || t.Right == x).ToList();
                if (aligned.Count > 0)
                {
                    lines.Add(new SnapLine(true, x, Math.Min(snapped.Top, aligned.Min(t => t.Top)), Math.Max(snapped.Bottom, aligned.Max(t => t.Bottom))));
                }
            }
        }

        if (dy is not null)
        {
            foreach (var y in ys.Select(y => y + dy.Value).Distinct())
            {
                var aligned = targets.Where(t => t.Top == y || t.CenterY == y || t.Bottom == y).ToList();
                if (aligned.Count > 0)
                {
                    lines.Add(new SnapLine(false, y, Math.Min(snapped.Left, aligned.Min(t => t.Left)), Math.Max(snapped.Right, aligned.Max(t => t.Right))));
                }
            }
        }

        return new SnapResult(dx ?? 0, dy ?? 0, dx is not null, dy is not null, lines);
    }

    /// <summary>The smallest shift (within the tolerance) that puts one of <paramref name="own"/> on one of <paramref name="candidates"/>.</summary>
    private static int? Best(int[] own, IEnumerable<int> candidates, int tolerance)
    {
        int? best = null;
        foreach (var candidate in candidates)
        {
            foreach (var value in own)
            {
                var delta = candidate - value;
                if (Math.Abs(delta) <= tolerance && (best is null || Math.Abs(delta) < Math.Abs(best.Value)))
                {
                    best = delta;
                }
            }
        }

        return best;
    }
}
