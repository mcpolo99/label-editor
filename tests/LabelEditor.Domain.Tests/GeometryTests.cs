using LabelEditor.Domain.Geometry;

namespace LabelEditor.Domain.Tests;

public class GeometryTests
{
    [Fact]
    public void Normalize_flips_negative_sizes()
    {
        Assert.Equal(new LabelRect(5, 2, 10, 8), new LabelRect(15, 10, -10, -8).Normalize());
    }

    [Theory]
    [InlineData(ResizeHandle.BottomRight, 5, 5, 0, 0, 25, 25)]
    [InlineData(ResizeHandle.TopLeft, 5, 5, 5, 5, 15, 15)]
    [InlineData(ResizeHandle.Left, -5, 99, -5, 0, 25, 20)]
    [InlineData(ResizeHandle.Top, 99, -5, 0, -5, 20, 25)]
    public void Resize_moves_only_the_dragged_edges(ResizeHandle handle, int dx, int dy, int x, int y, int w, int h)
    {
        Assert.Equal(new LabelRect(x, y, w, h), new LabelRect(0, 0, 20, 20).Resize(handle, dx, dy));
    }

    [Fact]
    public void Resize_clamps_at_the_minimum_instead_of_flipping()
    {
        var resized = new LabelRect(0, 0, 20, 20).Resize(ResizeHandle.Right, -50, 0, minimumSize: 2);

        Assert.Equal(new LabelRect(0, 0, 2, 20), resized);
    }

    [Fact]
    public void Distance_to_segment_handles_points_beyond_the_ends()
    {
        var line = new LabelRect(0, 0, 10, 0);

        Assert.Equal(3, line.DistanceToSegment(new LabelPoint(5, 3)));
        Assert.Equal(5, line.DistanceToSegment(new LabelPoint(13, 4)));
    }

    [Theory]
    [InlineData(1499, 1000, 1000)]
    [InlineData(1500, 1000, 2000)]
    [InlineData(-1500, 1000, -2000)]
    [InlineData(1234, 0, 1234)]
    public void Snap_rounds_to_the_nearest_grid_line(int value, int grid, int expected) =>
        Assert.Equal(expected, LayoutUnits.Snap(value, grid));

    [Fact]
    public void Inch_layout_units_convert_to_millimetres()
    {
        Assert.Equal(25.4, LayoutUnits.ToMillimetres(1000, MeasurementUnit.Inch), 6);
        Assert.Equal(1000, LayoutUnits.FromMillimetres(1, MeasurementUnit.Metric));
    }
}
