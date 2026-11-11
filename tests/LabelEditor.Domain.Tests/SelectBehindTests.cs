using LabelEditor.Domain.Editing;
using LabelEditor.Domain.Entries;
using LabelEditor.Domain.Geometry;
using LabelEditor.Domain.Layout;

namespace LabelEditor.Domain.Tests;

public class SelectBehindTests
{
    private static EllipseEntry Box(int x, int y, int size) => new() { Id = EntryId.New(), Bounds = new LabelRect(x, y, size, size) };

    // Drawn in this order: big at the back, small on top.
    private static readonly EllipseEntry Big = Box(0, 0, 50);
    private static readonly EllipseEntry Middle = Box(10, 10, 30);
    private static readonly EllipseEntry Small = Box(15, 15, 10);
    private static readonly EllipseEntry Elsewhere = Box(80, 80, 10);
    private static readonly LabelLayout Layout = LabelLayout.CreateNew().AddEntries([Big, Middle, Small, Elsewhere]);
    private static readonly LabelPoint Center = new(20, 20);

    [Fact]
    public void Lists_every_entry_under_the_point_topmost_first()
    {
        Assert.Equal([Small.Id, Middle.Id, Big.Id], HitTesting.EntriesAt(Layout, Center, 0).Select(e => e.Id));
    }

    [Fact]
    public void Select_behind_walks_down_the_stack_and_wraps_around()
    {
        var first = HitTesting.NextEntryAt(Layout, Center, 0, current: null);
        var second = HitTesting.NextEntryAt(Layout, Center, 0, first!.Id);
        var third = HitTesting.NextEntryAt(Layout, Center, 0, second!.Id);
        var wrapped = HitTesting.NextEntryAt(Layout, Center, 0, third!.Id);

        Assert.Equal([Small.Id, Middle.Id, Big.Id, Small.Id], new[] { first.Id, second.Id, third.Id, wrapped!.Id });
    }

    [Fact]
    public void Select_behind_starts_at_the_top_when_the_selection_is_not_under_the_point()
    {
        Assert.Equal(Small.Id, HitTesting.NextEntryAt(Layout, Center, 0, Elsewhere.Id)?.Id);
        Assert.Null(HitTesting.NextEntryAt(Layout, new LabelPoint(70, 5), 0, null));
    }
}
