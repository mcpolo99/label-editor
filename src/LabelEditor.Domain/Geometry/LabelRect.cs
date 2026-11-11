namespace LabelEditor.Domain.Geometry;

/// <summary>The grip being dragged when resizing an entry.</summary>
public enum ResizeHandle
{
    TopLeft,
    Top,
    TopRight,
    Right,
    BottomRight,
    Bottom,
    BottomLeft,
    Left,
}

/// <summary>
/// An entry's field rectangle in layout units, Y growing downwards.
/// <para>
/// Width and Height are signed on purpose: a line entry is the segment from (X, Y) to
/// (X + Width, Y + Height), so its direction lives in the signs. Box-shaped entries are
/// normally stored with non-negative sizes; use <see cref="Normalize"/> for box maths.
/// </para>
/// </summary>
public readonly record struct LabelRect(int X, int Y, int Width, int Height)
{
    public int Left => Math.Min(X, X + Width);

    public int Top => Math.Min(Y, Y + Height);

    public int Right => Math.Max(X, X + Width);

    public int Bottom => Math.Max(Y, Y + Height);

    public int CenterX => Left + ((Right - Left) / 2);

    public int CenterY => Top + ((Bottom - Top) / 2);

    public LabelPoint Start => new(X, Y);

    public LabelPoint End => new(X + Width, Y + Height);

    public static LabelRect FromEdges(int left, int top, int right, int bottom) =>
        new(Math.Min(left, right), Math.Min(top, bottom), Math.Abs(right - left), Math.Abs(bottom - top));

    public static LabelRect FromPoints(LabelPoint a, LabelPoint b) => FromEdges(a.X, a.Y, b.X, b.Y);

    /// <summary>Equivalent rectangle with non-negative width and height.</summary>
    public LabelRect Normalize() => new(Left, Top, Right - Left, Bottom - Top);

    public LabelRect Offset(int dx, int dy) => this with { X = X + dx, Y = Y + dy };

    public LabelRect Offset(LabelPoint delta) => Offset(delta.X, delta.Y);

    public bool Contains(LabelPoint p, int tolerance = 0) =>
        p.X >= Left - tolerance && p.X <= Right + tolerance &&
        p.Y >= Top - tolerance && p.Y <= Bottom + tolerance;

    public bool IntersectsWith(LabelRect other) =>
        Left <= other.Right && other.Left <= Right && Top <= other.Bottom && other.Top <= Bottom;

    public LabelRect Union(LabelRect other) =>
        FromEdges(Math.Min(Left, other.Left), Math.Min(Top, other.Top), Math.Max(Right, other.Right), Math.Max(Bottom, other.Bottom));

    /// <summary>Moves the given handle by (dx, dy), keeping the opposite edge(s) fixed.</summary>
    /// <param name="handle">The grip being dragged.</param>
    /// <param name="dx">Horizontal movement in layout units.</param>
    /// <param name="dy">Vertical movement in layout units.</param>
    /// <param name="minimumSize">Smallest allowed width/height after the resize.</param>
    /// <returns>The resized, normalized rectangle.</returns>
    public LabelRect Resize(ResizeHandle handle, int dx, int dy, int minimumSize = 0)
    {
        int left = Left, top = Top, right = Right, bottom = Bottom;

        switch (handle)
        {
            case ResizeHandle.TopLeft: left += dx; top += dy; break;
            case ResizeHandle.Top: top += dy; break;
            case ResizeHandle.TopRight: right += dx; top += dy; break;
            case ResizeHandle.Right: right += dx; break;
            case ResizeHandle.BottomRight: right += dx; bottom += dy; break;
            case ResizeHandle.Bottom: bottom += dy; break;
            case ResizeHandle.BottomLeft: left += dx; bottom += dy; break;
            case ResizeHandle.Left: left += dx; break;
            default: throw new ArgumentOutOfRangeException(nameof(handle), handle, null);
        }

        // Clamp instead of flipping: dragging past the opposite edge stops at the minimum size.
        if (right - left < minimumSize)
        {
            if (IsLeftHandle(handle)) { left = right - minimumSize; } else { right = left + minimumSize; }
        }

        if (bottom - top < minimumSize)
        {
            if (IsTopHandle(handle)) { top = bottom - minimumSize; } else { bottom = top + minimumSize; }
        }

        return new LabelRect(left, top, right - left, bottom - top);
    }

    /// <summary>Shortest distance from <paramref name="p"/> to the segment Start→End (used for line hit-testing).</summary>
    /// <param name="p">The probe point.</param>
    /// <returns>Distance in layout units.</returns>
    public double DistanceToSegment(LabelPoint p)
    {
        double x1 = X, y1 = Y, x2 = X + Width, y2 = Y + Height;
        double dx = x2 - x1, dy = y2 - y1;
        var lengthSquared = (dx * dx) + (dy * dy);
        var t = lengthSquared == 0 ? 0 : Math.Clamp((((p.X - x1) * dx) + ((p.Y - y1) * dy)) / lengthSquared, 0, 1);
        var cx = x1 + (t * dx) - p.X;
        var cy = y1 + (t * dy) - p.Y;
        return Math.Sqrt((cx * cx) + (cy * cy));
    }

    private static bool IsLeftHandle(ResizeHandle h) => h is ResizeHandle.TopLeft or ResizeHandle.Left or ResizeHandle.BottomLeft;

    private static bool IsTopHandle(ResizeHandle h) => h is ResizeHandle.TopLeft or ResizeHandle.Top or ResizeHandle.TopRight;
}
