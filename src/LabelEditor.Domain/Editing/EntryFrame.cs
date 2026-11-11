using LabelEditor.Domain.Entries;
using LabelEditor.Domain.Geometry;
using LabelEditor.Domain.Text;

namespace LabelEditor.Domain.Editing;

/// <summary>
/// The entry's own frame: "along" runs in the reading direction of its text, "across" from the top of the letters
/// to their bottom. For an unrotated entry that is plain X / Y; a text rotated by 90° reads along the label's Y axis.
/// Sizing several entries together works in this frame, so "same width" means the same text length whichever way
/// each entry is turned.
/// </summary>
public static class EntryFrame
{
    /// <summary>How the content of an entry is turned. Only text (and bar codes) can be rotated.</summary>
    public static TextRotation RotationOf(LabelEntry entry) => entry is TextEntry t ? t.Font.Rotation : TextRotation.None;

    public static bool IsQuarterTurn(TextRotation rotation) => rotation is TextRotation.Ccw90 or TextRotation.Cw90;

    /// <summary>Size along and across the text.</summary>
    public static (int Along, int Across) LocalSize(LabelEntry entry)
    {
        var b = entry.Bounds.Normalize();
        return IsQuarterTurn(RotationOf(entry)) ? (b.Height, b.Width) : (b.Width, b.Height);
    }

    /// <summary>A label-space vector (Y down) expressed in the entry frame.</summary>
    public static (int Along, int Across) ToLocal(TextRotation rotation, int dx, int dy) => rotation switch
    {
        TextRotation.Ccw90 => (-dy, dx), // reads bottom to top, letter tops face left
        TextRotation.Cw90 => (dy, -dx), // reads top to bottom, letter tops face right
        TextRotation.Rotate180 => (-dx, -dy),
        _ => (dx, dy),
    };

    /// <summary>Inverse of <see cref="ToLocal"/>.</summary>
    public static (int Dx, int Dy) ToLabel(TextRotation rotation, int along, int across) => rotation switch
    {
        TextRotation.Ccw90 => (across, -along),
        TextRotation.Cw90 => (-across, along),
        TextRotation.Rotate180 => (-along, -across),
        _ => (along, across),
    };

    /// <summary>
    /// Resizes <paramref name="entry"/> by dragging <paramref name="handle"/> of an entry turned by
    /// <paramref name="sourceRotation"/> by (<paramref name="dx"/>, <paramref name="dy"/>): the drag is applied in the
    /// entry's own frame, so a longer text on one entry makes the others longer along their own text direction.
    /// </summary>
    public static LabelRect ResizeLike(LabelEntry entry, TextRotation sourceRotation, ResizeHandle handle, int dx, int dy, int minimumSize = 0)
    {
        var (hx, hy) = Direction(handle);
        var rotation = RotationOf(entry);
        var (lhx, lhy) = ToLocal(sourceRotation, hx, hy);
        var (ldx, ldy) = ToLocal(sourceRotation, dx, dy);
        var (thx, thy) = ToLabel(rotation, lhx, lhy);
        var (tdx, tdy) = ToLabel(rotation, ldx, ldy);
        return Resize(entry, thx, thy, tdx, tdy, minimumSize);
    }

    /// <summary>
    /// Resizes by one of the entry's own handles. A position-locked entry keeps its top-left corner: a left / top
    /// handle then resizes from the right / bottom by the same amount.
    /// </summary>
    public static LabelRect Resize(LabelEntry entry, ResizeHandle handle, int dx, int dy, int minimumSize = 0)
    {
        var (hx, hy) = Direction(handle);
        return Resize(entry, hx, hy, dx, dy, minimumSize);
    }

    /// <summary>
    /// Sets the size along / across the text, keeping the corner where the text starts in place (top-left for
    /// unrotated text; the top-left corner for a position-locked entry).
    /// </summary>
    public static LabelRect WithLocalSize(LabelEntry entry, int along, int across)
    {
        var b = entry.Bounds.Normalize();
        var rotation = RotationOf(entry);
        var (width, height) = IsQuarterTurn(rotation) ? (across, along) : (along, across);
        if (entry.IsPositionLocked)
        {
            return new LabelRect(b.Left, b.Top, width, height);
        }

        // The corner the text starts from, in the label: the origin of the entry frame.
        var keepRight = rotation is TextRotation.Cw90 or TextRotation.Rotate180;
        var keepBottom = rotation is TextRotation.Ccw90 or TextRotation.Rotate180;
        var left = keepRight ? b.Right - width : b.Left;
        var top = keepBottom ? b.Bottom - height : b.Top;
        return new LabelRect(left, top, width, height);
    }

    private static LabelRect Resize(LabelEntry entry, int hx, int hy, int dx, int dy, int minimumSize)
    {
        if (entry.IsPositionLocked)
        {
            // Only the right and bottom edges may move: a change at the left / top is applied to the opposite edge,
            // so the entry still grows or shrinks by the same amount, away from its fixed corner.
            if (hx < 0)
            {
                (hx, dx) = (1, -dx);
            }

            if (hy < 0)
            {
                (hy, dy) = (1, -dy);
            }
        }

        return ToHandle(hx, hy) is { } handle ? entry.Bounds.Resize(handle, hx == 0 ? 0 : dx, hy == 0 ? 0 : dy, minimumSize) : entry.Bounds;
    }

    private static (int X, int Y) Direction(ResizeHandle handle) => handle switch
    {
        ResizeHandle.TopLeft => (-1, -1),
        ResizeHandle.Top => (0, -1),
        ResizeHandle.TopRight => (1, -1),
        ResizeHandle.Right => (1, 0),
        ResizeHandle.BottomRight => (1, 1),
        ResizeHandle.Bottom => (0, 1),
        ResizeHandle.BottomLeft => (-1, 1),
        ResizeHandle.Left => (-1, 0),
        _ => (0, 0),
    };

    private static ResizeHandle? ToHandle(int x, int y) => (x, y) switch
    {
        (-1, -1) => ResizeHandle.TopLeft,
        (0, -1) => ResizeHandle.Top,
        (1, -1) => ResizeHandle.TopRight,
        (1, 0) => ResizeHandle.Right,
        (1, 1) => ResizeHandle.BottomRight,
        (0, 1) => ResizeHandle.Bottom,
        (-1, 1) => ResizeHandle.BottomLeft,
        (-1, 0) => ResizeHandle.Left,
        _ => null,
    };
}
