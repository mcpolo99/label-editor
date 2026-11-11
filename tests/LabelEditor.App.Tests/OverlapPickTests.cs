using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LabelEditor.App.Controls;
using LabelEditor.App.Rendering;
using LabelEditor.App.Views;
using LabelEditor.Application.Editing;
using LabelEditor.Application.Settings;
using LabelEditor.Domain.Entries;
using LabelEditor.Domain.Geometry;
using LabelEditor.Infrastructure.Storage;
using LabelEditor.Presentation.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace LabelEditor.App.Tests;

/// <summary>Drives the real canvas with mouse input: clicking overlapping entries offers a pick list.</summary>
public class OverlapPickTests
{
    private static async Task<(MainWindow Window, ServiceProvider Services, DocumentViewModel Document, LabelCanvas Canvas, EllipseEntry Back, EllipseEntry Front)> SetupAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "LabelEditorUiTests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(AppContext.BaseDirectory, root, IsPortable: true);
        var storage = new StorageOptions
        {
            PackagedDefaultsRoot = Path.Combine(AppContext.BaseDirectory, "Defaults", "LEdit"),
            MachineInstallationRoot = null,
            WorkingFolder = Path.Combine(root, "Labels"),
        };
        var window = new MainWindow { Width = 1400, Height = 860 };
        var services = App.ConfigureServices(paths, storage, window);
        var vm = services.GetRequiredService<MainViewModel>();
        window.DataContext = vm;
        window.Show();
        await vm.InitializeAsync(new EditorSettings { Language = "English", SnapToGrid = false }, []);

        var document = vm.ActiveDocument!;
        var back = new EllipseEntry { Id = EntryId.New(), Bounds = new LabelRect(10_000, 10_000, 30_000, 30_000) };
        var front = new EllipseEntry { Id = EntryId.New(), Bounds = new LabelRect(20_000, 20_000, 10_000, 10_000) };
        document.Session.AddEntry(back);
        document.Session.AddEntry(front);
        document.Session.Selection.Clear();
        Dispatcher.UIThread.RunJobs();
        var canvas = window.GetVisualDescendants().OfType<LabelCanvas>().Single();
        return (window, services, document, canvas, back, front);
    }

    private static Point OnScreen(MainWindow window, LabelCanvas canvas, DocumentViewModel document, int x, int y)
    {
        var view = new ViewTransform(ViewTransform.ScaleFor(document.Zoom, MeasurementUnit.Metric), new Point(24, 24));
        return canvas.TranslatePoint(view.ToView(x, y), window)!.Value;
    }

    [AvaloniaFact]
    public async Task Clicking_where_entries_overlap_opens_the_pick_list_and_selects_the_top_one()
    {
        var (window, services, document, canvas, _, front) = await SetupAsync();
        var p = OnScreen(window, canvas, document, 25_000, 25_000);

        window.MouseDown(p, MouseButton.Left);
        window.MouseUp(p, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.True(canvas.IsPickListOpen);
        Assert.Equal([front.Id], document.Session.Selection.Ids);
        Assert.Equal(2, document.EntriesAt(new LabelPoint(25_000, 25_000), 0).Count);

        // What the user sees: the list at the cursor, with the hovered (back) entry outlined.
        document.HighlightedEntry = document.EntriesAt(new LabelPoint(25_000, 25_000), 0)[1].Id;
        Dispatcher.UIThread.RunJobs();
        var file = Path.Combine(AppContext.BaseDirectory, "rendered", "overlap-pick.png");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        window.CaptureRenderedFrame()?.Save(file);
        window.Close();
        await services.DisposeAsync();
    }

    [AvaloniaFact]
    public async Task Clicking_a_single_entry_just_selects_it()
    {
        var (window, services, document, canvas, back, _) = await SetupAsync();
        var p = OnScreen(window, canvas, document, 12_000, 12_000); // only the back ellipse is here

        window.MouseDown(p, MouseButton.Left);
        window.MouseUp(p, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.False(canvas.IsPickListOpen);
        Assert.Equal([back.Id], document.Session.Selection.Ids);
        window.Close();
        await services.DisposeAsync();
    }

    [AvaloniaFact]
    public async Task An_entry_picked_from_behind_is_the_one_that_gets_dragged()
    {
        var (window, services, document, canvas, back, front) = await SetupAsync();
        document.SelectEntry(back.Id); // as if chosen in the pick list
        var start = OnScreen(window, canvas, document, 25_000, 25_000);
        var end = start + new Vector(40, 0);

        window.MouseDown(start, MouseButton.Left);
        window.MouseMove(start + new Vector(10, 0), RawInputModifiers.LeftMouseButton);
        window.MouseMove(end, RawInputModifiers.LeftMouseButton);
        window.MouseUp(end, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.False(canvas.IsPickListOpen); // a drag never opens the list
        Assert.True(document.Layout.Find(back.Id)!.Bounds.X > 10_000, "the selected entry behind should have moved");
        Assert.Equal(20_000, document.Layout.Find(front.Id)!.Bounds.X);
        window.Close();
        await services.DisposeAsync();
    }

    [AvaloniaFact]
    public async Task Shift_clicking_overlapping_entries_offers_the_list_without_changing_the_selection_yet()
    {
        var (window, services, document, canvas, back, front) = await SetupAsync();
        var other = new EllipseEntry { Id = EntryId.New(), Bounds = new LabelRect(60_000, 10_000, 5_000, 5_000) };
        document.Session.AddEntry(other); // selected: an entry somewhere else
        var p = OnScreen(window, canvas, document, 25_000, 25_000);

        window.MouseDown(p, MouseButton.Left, RawInputModifiers.Shift);
        window.MouseUp(p, MouseButton.Left, RawInputModifiers.Shift);
        Dispatcher.UIThread.RunJobs();

        Assert.True(canvas.IsPickListOpen);
        Assert.Equal([other.Id], document.Session.Selection.Ids); // nothing added until the user picks

        document.SelectEntry(back.Id, Application.Documents.SelectionMode.Add); // what picking "back" in the list does
        Assert.Equal([other.Id, back.Id], document.Session.Selection.Ids);
        Assert.DoesNotContain(front.Id, document.Session.Selection.Ids);
        window.Close();
        await services.DisposeAsync();
    }

    [AvaloniaFact]
    public async Task Ctrl_clicking_overlapping_entries_offers_the_list_too()
    {
        var (window, services, document, canvas, back, _) = await SetupAsync();
        document.Session.Selection.Select(back.Id);
        var p = OnScreen(window, canvas, document, 25_000, 25_000);

        window.MouseDown(p, MouseButton.Left, RawInputModifiers.Control);
        window.MouseUp(p, MouseButton.Left, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();

        Assert.True(canvas.IsPickListOpen);
        Assert.Equal([back.Id], document.Session.Selection.Ids);
        document.SelectEntry(back.Id, Application.Documents.SelectionMode.Toggle);
        Assert.Empty(document.Session.Selection.Ids);
        window.Close();
        await services.DisposeAsync();
    }

    [AvaloniaFact]
    public async Task An_entry_does_not_stick_to_the_cursor_when_the_button_release_was_missed()
    {
        // E.g. a dialog opened during the click and took the release: moving afterwards must not drag the entry.
        var (window, services, document, canvas, back, _) = await SetupAsync();
        var start = OnScreen(window, canvas, document, 12_000, 12_000);

        window.MouseDown(start, MouseButton.Left);
        window.MouseMove(start + new Vector(40, 0), RawInputModifiers.None); // button no longer held
        window.MouseMove(start + new Vector(80, 0), RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(10_000, document.Layout.Find(back.Id)!.Bounds.X);
        Assert.False(document.Session.IsInteracting);
        window.Close();
        await services.DisposeAsync();
    }

    [AvaloniaFact]
    public async Task Shift_clicking_a_single_entry_still_adds_it_directly()
    {
        var (window, services, document, canvas, back, front) = await SetupAsync();
        document.Session.Selection.Select(front.Id);
        var p = OnScreen(window, canvas, document, 12_000, 12_000); // only the back ellipse

        window.MouseDown(p, MouseButton.Left, RawInputModifiers.Shift);
        window.MouseUp(p, MouseButton.Left, RawInputModifiers.Shift);
        Dispatcher.UIThread.RunJobs();

        Assert.False(canvas.IsPickListOpen);
        Assert.Equal([front.Id, back.Id], document.Session.Selection.Ids);
        window.Close();
        await services.DisposeAsync();
    }
}
