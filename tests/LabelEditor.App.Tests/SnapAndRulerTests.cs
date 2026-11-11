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

/// <summary>Dragging with snap-to-entries guides, and the rulers around the canvas.</summary>
public class SnapAndRulerTests
{
    private static async Task<(MainWindow Window, ServiceProvider Services, DocumentViewModel Document, LabelCanvas Canvas)> SetupAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "LabelEditorUiTests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(AppContext.BaseDirectory, root, IsPortable: true);
        var storage = new StorageOptions
        {
            MachineInstallationRoot = Path.Combine(AppContext.BaseDirectory, "TestLEdit"),
            WorkingFolder = Path.Combine(root, "Labels"),
        };
        var window = new MainWindow { Width = 1400, Height = 860 };
        var services = App.ConfigureServices(paths, storage, window);
        var vm = services.GetRequiredService<MainViewModel>();
        window.DataContext = vm;
        window.Show();
        await vm.InitializeAsync(new EditorSettings { Language = "English", SnapToGrid = false }, []);
        var document = vm.ActiveDocument!;
        Dispatcher.UIThread.RunJobs();
        return (window, services, document, window.GetVisualDescendants().OfType<LabelCanvas>().Single());
    }

    private static Point OnScreen(MainWindow window, LabelCanvas canvas, DocumentViewModel document, int x, int y)
    {
        var view = new ViewTransform(ViewTransform.ScaleFor(document.Zoom, MeasurementUnit.Metric), new Point(LabelCanvas.CanvasMargin, LabelCanvas.CanvasMargin));
        return canvas.TranslatePoint(view.ToView(x, y), window)!.Value;
    }

    private static (EllipseEntry Anchor, EllipseEntry Moving) AddTwo(DocumentViewModel document)
    {
        var anchor = new EllipseEntry { Id = EntryId.New(), Bounds = new LabelRect(10_000, 10_000, 30_000, 20_000) };
        var moving = new EllipseEntry { Id = EntryId.New(), Bounds = new LabelRect(60_000, 45_000, 5_000, 5_000) };
        document.Session.AddEntry(anchor);
        document.Session.AddEntry(moving);
        document.Session.Selection.Clear();
        Dispatcher.UIThread.RunJobs();
        return (anchor, moving);
    }

    /// <summary>Drags the moving ellipse by (dx, dy) layout units, pausing before the release so a frame can be captured.</summary>
    private static void Drag(MainWindow window, LabelCanvas canvas, DocumentViewModel document, int dx, int dy, RawInputModifiers modifiers = RawInputModifiers.None, string? capture = null)
    {
        var start = OnScreen(window, canvas, document, 62_500, 47_500);
        var end = OnScreen(window, canvas, document, 62_500 + dx, 47_500 + dy);
        window.MouseDown(start, MouseButton.Left, modifiers);
        window.MouseMove(start + new Vector(10, 0), RawInputModifiers.LeftMouseButton | modifiers);
        window.MouseMove(end, RawInputModifiers.LeftMouseButton | modifiers);
        Dispatcher.UIThread.RunJobs();
        if (capture is not null)
        {
            var file = Path.Combine(AppContext.BaseDirectory, "rendered", capture);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            window.CaptureRenderedFrame()?.Save(file);
        }

        window.MouseUp(end, MouseButton.Left, modifiers);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public async Task A_dragged_entry_lines_up_with_another_entrys_left_edge()
    {
        var (window, services, document, canvas) = await SetupAsync();
        var (_, moving) = AddTwo(document);

        Drag(window, canvas, document, -49_700, 0, capture: "snap-guides.png"); // left edge ends 0.3 mm right of the other's

        Assert.Equal(10_000, document.Layout.Find(moving.Id)!.Bounds.X);
        window.Close();
        await services.DisposeAsync();
    }

    [AvaloniaFact]
    public async Task Holding_alt_moves_freely_without_snapping()
    {
        var (window, services, document, canvas) = await SetupAsync();
        var (_, moving) = AddTwo(document);

        Drag(window, canvas, document, -49_700, 0, RawInputModifiers.Alt);

        Assert.InRange(document.Layout.Find(moving.Id)!.Bounds.X, 10_150, 10_450);
        window.Close();
        await services.DisposeAsync();
    }

    [AvaloniaFact]
    public async Task Snapping_to_entries_can_be_turned_off()
    {
        var (window, services, document, canvas) = await SetupAsync();
        var (_, moving) = AddTwo(document);
        document.Context.SnapToEntries = false;

        Drag(window, canvas, document, -49_700, 0);

        Assert.NotEqual(10_000, document.Layout.Find(moving.Id)!.Bounds.X);
        window.Close();
        await services.DisposeAsync();
    }

    [AvaloniaFact]
    public async Task Rulers_are_shown_along_the_canvas_and_can_be_hidden()
    {
        var (window, services, document, _) = await SetupAsync();
        var rulers = window.GetVisualDescendants().OfType<Ruler>().ToList();

        Assert.Equal(2, rulers.Count);
        Assert.All(rulers, r => Assert.True(r.IsEffectivelyVisible));
        Assert.All(rulers, r => Assert.Same(document, r.Document));

        document.Context.ShowRulers = false;
        Dispatcher.UIThread.RunJobs();
        Assert.All(rulers, r => Assert.False(r.IsEffectivelyVisible));
        window.Close();
        await services.DisposeAsync();
    }
}
