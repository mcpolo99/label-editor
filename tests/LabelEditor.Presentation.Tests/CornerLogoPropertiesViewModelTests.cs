using LabelEditor.Application.Documents;
using LabelEditor.Domain.Entries;
using LabelEditor.Domain.Geometry;
using LabelEditor.Domain.Layout;
using LabelEditor.Presentation.ViewModels.Properties;

namespace LabelEditor.Presentation.Tests;

public class CornerLogoPropertiesViewModelTests
{
    private static (EditorSession Session, CornerLogoEntry Entry, CornerLogoPropertiesViewModel Vm) Setup(int? orientation)
    {
        var entry = new CornerLogoEntry { Id = EntryId.New(), Bounds = new LabelRect(0, 0, 30_000, 20_000), Orientation = orientation };
        var session = new EditorSession(LabelLayout.CreateNew().AddEntry(entry));
        return (session, entry, new CornerLogoPropertiesViewModel(session, entry));
    }

    [Theory]
    [InlineData(null, 0, "(not set)")]
    [InlineData(0, 1, "0°")]
    [InlineData(1, 2, "+90°")]
    [InlineData(2, 3, "−90°")]
    [InlineData(3, 4, "180°")]
    public void Orientation_shows_ledits_radio_buttons(int? orient, int index, string caption)
    {
        var (_, _, vm) = Setup(orient);

        Assert.Equal(index, vm.OrientationIndex);
        Assert.Equal(caption, vm.OrientationOptions[index]);
    }

    [Fact]
    public void Choosing_an_orientation_stores_ledits_value_and_not_set_removes_it()
    {
        var (session, entry, vm) = Setup(0);

        vm.OrientationIndex = 4;
        Assert.Equal(3, ((CornerLogoEntry)session.Layout.Find(entry.Id)!).Orientation);

        vm.OrientationIndex = 0;
        Assert.Null(((CornerLogoEntry)session.Layout.Find(entry.Id)!).Orientation);
    }
}
