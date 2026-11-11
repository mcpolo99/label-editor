using LabelEditor.Domain.Editing;
using LabelEditor.Domain.Entries;
using LabelEditor.Domain.Geometry;
using LabelEditor.Domain.Layout;
using LabelEditor.Domain.Text;

namespace LabelEditor.Domain.Tests;

/// <summary>Sizing in each entry's own text frame, and the position lock.</summary>
public class EntryFrameTests
{
    private static TextEntry Text(int x, int y, int w, int h, TextRotation rotation = TextRotation.None)
    {
        var entry = new TextEntry { Id = EntryId.New(), Bounds = new LabelRect(x, y, w, h) };
        return entry with { Font = entry.Font.WithRotation(rotation) };
    }

    private static LabelLayout With(params LabelEntry[] entries) => LabelLayout.CreateNew().AddEntries(entries);

    [Theory]
    [InlineData(TextRotation.None, 3, 4, 3, 4)]
    [InlineData(TextRotation.Ccw90, 3, 4, -4, 3)]
    [InlineData(TextRotation.Cw90, 3, 4, 4, -3)]
    [InlineData(TextRotation.Rotate180, 3, 4, -3, -4)]
    public void Label_and_entry_frames_convert_both_ways(TextRotation rotation, int dx, int dy, int along, int across)
    {
        Assert.Equal((along, across), EntryFrame.ToLocal(rotation, dx, dy));
        Assert.Equal((dx, dy), EntryFrame.ToLabel(rotation, along, across));
    }

    [Fact]
    public void Same_width_gives_a_rotated_text_the_reference_length_along_its_reading_direction()
    {
        var reference = Text(0, 0, 40_000, 8_000);
        var turned = Text(50_000, 20_000, 10_000, 30_000, TextRotation.Ccw90); // reads bottom to top

        var result = LayoutArrangement.MatchSize(With(reference, turned), [reference.Id, turned.Id], SizeMatch.Width);

        // Longer along the text (taller), same thickness, and the text still starts at the same (bottom) edge.
        Assert.Equal(new LabelRect(50_000, 10_000, 10_000, 40_000), result.Find(turned.Id)!.Bounds);
    }

    [Fact]
    public void Same_size_maps_width_and_height_through_the_rotation()
    {
        var reference = Text(0, 0, 40_000, 8_000);
        var turned = Text(50_000, 20_000, 10_000, 30_000, TextRotation.Cw90); // reads top to bottom

        var result = LayoutArrangement.MatchSize(With(reference, turned), [reference.Id, turned.Id], SizeMatch.Both);

        // Cw90 text starts at the top-right corner, which stays put.
        Assert.Equal(new LabelRect(52_000, 20_000, 8_000, 40_000), result.Find(turned.Id)!.Bounds);
    }

    [Fact]
    public void A_rotated_reference_gives_unrotated_entries_its_text_length_as_width()
    {
        var reference = Text(0, 0, 10_000, 30_000, TextRotation.Ccw90);
        var plain = Text(50_000, 0, 20_000, 5_000);

        var result = LayoutArrangement.MatchSize(With(reference, plain), [reference.Id, plain.Id], SizeMatch.Width);

        Assert.Equal(new LabelRect(50_000, 0, 30_000, 5_000), result.Find(plain.Id)!.Bounds);
    }

    [Fact]
    public void Dragging_one_handle_maps_onto_the_matching_edge_of_a_rotated_entry()
    {
        var turned = Text(50_000, 20_000, 10_000, 30_000, TextRotation.Ccw90);

        // The reference's right edge (end of its text) moved 5 mm right: the turned text gets 5 mm longer at its end (top).
        var bounds = EntryFrame.ResizeLike(turned, TextRotation.None, ResizeHandle.Right, 5_000, 0);

        Assert.Equal(new LabelRect(50_000, 15_000, 10_000, 35_000), bounds);
    }

    [Fact]
    public void Position_locked_entries_stay_put_for_moves_and_arrange_commands()
    {
        var reference = Text(0, 0, 10_000, 10_000);
        var pinned = Text(30_000, 40_000, 10_000, 10_000) with { IsPositionLocked = true };
        var layout = With(reference, pinned);
        var ids = new HashSet<EntryId> { reference.Id, pinned.Id };

        Assert.Equal(pinned.Bounds, layout.MoveEntries(ids, 1_000, 1_000).Find(pinned.Id)!.Bounds);
        Assert.Equal(new LabelRect(1_000, 1_000, 10_000, 10_000), layout.MoveEntries(ids, 1_000, 1_000).Find(reference.Id)!.Bounds);
        Assert.Equal(pinned.Bounds, LayoutArrangement.Align(layout, [reference.Id, pinned.Id], Alignment.Left).Find(pinned.Id)!.Bounds);
        Assert.Equal(pinned.Bounds, LayoutArrangement.SnapToGrid(layout, [pinned.Id], 3_000).Find(pinned.Id)!.Bounds);
    }

    [Fact]
    public void A_position_locked_entry_resizes_away_from_its_fixed_corner()
    {
        var pinned = Text(30_000, 40_000, 10_000, 10_000) with { IsPositionLocked = true };

        Assert.Equal(new LabelRect(30_000, 40_000, 12_000, 10_000), EntryFrame.Resize(pinned, ResizeHandle.Left, -2_000, 0));
        Assert.Equal(new LabelRect(30_000, 40_000, 10_000, 13_000), EntryFrame.Resize(pinned, ResizeHandle.Bottom, 0, 3_000));

        var turned = Text(30_000, 40_000, 10_000, 10_000, TextRotation.Ccw90) with { IsPositionLocked = true };
        var sized = LayoutArrangement.MatchSize(With(Text(0, 0, 25_000, 10_000), turned), [], SizeMatch.Width);
        Assert.Equal(turned.Bounds, sized.Find(turned.Id)!.Bounds); // no selection, no change
        Assert.Equal(new LabelRect(30_000, 40_000, 10_000, 25_000), EntryFrame.WithLocalSize(turned, 25_000, 10_000));
    }
}
