namespace LabelEditor.Domain.Geometry;

/// <summary>
/// A point (or offset) in layout units. Layout units are 1/1000 of the label's measurement unit
/// (micrometres for metric labels). The domain uses screen orientation: Y grows downwards.
/// </summary>
public readonly record struct LabelPoint(int X, int Y)
{
    public static LabelPoint Zero => default;

    public static LabelPoint operator +(LabelPoint a, LabelPoint b) => new(a.X + b.X, a.Y + b.Y);

    public static LabelPoint operator -(LabelPoint a, LabelPoint b) => new(a.X - b.X, a.Y - b.Y);

    public LabelPoint Add(LabelPoint other) => this + other;

    public LabelPoint Subtract(LabelPoint other) => this - other;
}
