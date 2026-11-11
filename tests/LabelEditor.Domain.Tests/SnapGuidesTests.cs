using LabelEditor.Domain.Editing;
using LabelEditor.Domain.Entries;
using LabelEditor.Domain.Geometry;
using LabelEditor.Domain.Layout;

namespace LabelEditor.Domain.Tests;

public class SnapGuidesTests
{
    private static readonly LabelRect Other = new(10_000, 10_000, 20_000, 10_000); // left 10, centre 20, right 30 (mm)

    [Fact]
    public void A_box_dragged_near_another_entrys_left_edge_lines_up_with_it()
    {
        var moving = new LabelRect(10_400, 40_000, 15_000, 5_000);

        var result = SnapGuides.ForMove(moving, [Other], tolerance: 500);

        Assert.Equal((-400, 0, true, false), (result.Dx, result.Dy, result.SnappedX, result.SnappedY));
        var line = Assert.Single(result.Lines);
        Assert.Equal(new SnapLine(true, 10_000, 10_000, 45_000), line); // from the other entry's top to the moved box's bottom
    }

    [Fact]
    public void Centres_and_opposite_edges_line_up_too()
    {
        // Moving centre 20_300 vs other centre 20_000; moving top 20_200 vs other bottom 20_000.
        var moving = new LabelRect(15_300, 20_200, 10_000, 4_000);

        var result = SnapGuides.ForMove(moving, [Other], tolerance: 500);

        Assert.Equal((-300, -200), (result.Dx, result.Dy));
        Assert.Contains(result.Lines, l => l is { IsVertical: true, Position: 20_000 });
        Assert.Contains(result.Lines, l => l is { IsVertical: false, Position: 20_000 });
    }

    [Fact]
    public void The_nearest_alignment_wins()
    {
        var near = new LabelRect(0, 50_000, 5_000, 5_000);   // right edge 5_000
        var moving = new LabelRect(5_100, 0, 4_850, 3_000);  // left 5_100 (100 off), right 9_950 (50 off the other's left 10_000)

        var result = SnapGuides.ForMove(moving, [near, Other], tolerance: 500);

        Assert.Equal(50, result.Dx);
    }

    [Fact]
    public void Nothing_snaps_outside_the_tolerance()
    {
        var result = SnapGuides.ForMove(new LabelRect(11_000, 40_000, 15_000, 5_000), [Other], tolerance: 500);

        Assert.Same(SnapResult.None, result);
    }

    [Fact]
    public void Resizing_snaps_only_the_coordinates_the_handle_moves()
    {
        var right = SnapGuides.ForPoint(new LabelPoint(29_700, 15_100), movesX: true, movesY: false, [Other], tolerance: 500);
        Assert.Equal((300, 0, true, false), (right.Dx, right.Dy, right.SnappedX, right.SnappedY));

        var corner = SnapGuides.ForPoint(new LabelPoint(29_700, 19_800), movesX: true, movesY: true, [Other], tolerance: 500);
        Assert.Equal((300, 200), (corner.Dx, corner.Dy));
    }

    [Fact]
    public void Targets_are_the_label_and_the_entries_not_being_dragged()
    {
        var a = new EllipseEntry { Id = EntryId.New(), Bounds = Other };
        var b = new EllipseEntry { Id = EntryId.New(), Bounds = new LabelRect(50_000, 0, 5_000, 5_000) };
        var layout = LabelLayout.CreateNew().AddEntries([a, b]);

        var targets = SnapGuides.TargetsFor(layout, [b.Id]);

        Assert.Equal([layout.Header.Shape.Bounds, Other], targets);
    }

    [Fact]
    public void The_label_centre_is_a_target()
    {
        var layout = LabelLayout.CreateNew();
        var width = layout.Header.Shape.Width;
        var moving = new LabelRect((width / 2) - 5_000 + 200, 30_000, 10_000, 4_000); // centre 200 right of the label's

        var result = SnapGuides.ForMove(moving, SnapGuides.TargetsFor(layout, []), tolerance: 500);

        Assert.Equal(-200, result.Dx);
    }
}
