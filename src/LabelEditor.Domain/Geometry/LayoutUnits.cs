namespace LabelEditor.Domain.Geometry;

/// <summary>Label measurement unit (LEdit <c>Miscellaneous/@unit</c>).</summary>
public enum MeasurementUnit
{
    /// <summary>Layout units are 1/1000 mm.</summary>
    Metric = 0,

    /// <summary>Layout units are 1/1000 inch.</summary>
    Inch = 1,
}

/// <summary>Conversions between layout units, display units and millimetres.</summary>
public static class LayoutUnits
{
    /// <summary>Layout units per display unit (mm or inch).</summary>
    public const int PerUnit = 1000;

    public const double MillimetresPerInch = 25.4;

    public static double ToMillimetres(int value, MeasurementUnit unit) =>
        unit == MeasurementUnit.Inch ? value / (double)PerUnit * MillimetresPerInch : value / (double)PerUnit;

    public static int FromMillimetres(double millimetres, MeasurementUnit unit) =>
        (int)Math.Round(unit == MeasurementUnit.Inch ? millimetres / MillimetresPerInch * PerUnit : millimetres * PerUnit);

    /// <summary>Converts to the label's display unit (mm or inch).</summary>
    public static double ToDisplay(int value) => value / (double)PerUnit;

    public static int FromDisplay(double value) => (int)Math.Round(value * PerUnit);

    /// <summary>Rounds <paramref name="value"/> to the nearest multiple of <paramref name="grid"/>; a grid of 0 or less leaves it unchanged.</summary>
    public static int Snap(int value, int grid) => grid <= 0 ? value : (int)Math.Round(value / (double)grid, MidpointRounding.AwayFromZero) * grid;

    public static LabelPoint Snap(LabelPoint p, int grid) => new(Snap(p.X, grid), Snap(p.Y, grid));
}
