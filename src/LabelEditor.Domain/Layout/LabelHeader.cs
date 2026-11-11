using LabelEditor.Domain.Geometry;

namespace LabelEditor.Domain.Layout;

/// <summary>LEdit layout format version (<c>Header/Version</c>).</summary>
public readonly record struct FormatVersion(int Major, int Minor)
{
    /// <summary>The version written by current LEdit releases.</summary>
    public static FormatVersion Current => new(1, 34);

    public override string ToString() => $"{Major}.{Minor}";
}

/// <summary>Label outline (<c>Header/RectLabel</c>). Sizes are positive layout units.</summary>
public sealed record LabelShape
{
    /// <summary>LEdit shape code; 1 = rectangle (the only one seen).</summary>
    public int ShapeType { get; init; } = 1;

    public int Width { get; init; } = 100_000;

    public int Height { get; init; } = 60_000;

    public LabelRect Bounds => new(0, 0, Width, Height);
}

/// <summary>Layout-wide settings.</summary>
public sealed record LabelHeader
{
    public FormatVersion Version { get; init; } = FormatVersion.Current;

    /// <summary><c>Miscellaneous/@default</c>; kept verbatim (always 0 in known files).</summary>
    public int DefaultFlag { get; init; }

    public MeasurementUnit Unit { get; init; } = MeasurementUnit.Metric;

    /// <summary>Grid step in layout units; 0 = no grid.</summary>
    public int GridSize { get; init; } = 1000;

    public LabelShape Shape { get; init; } = new();
}
