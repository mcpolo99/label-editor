using Avalonia.VisualTree;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using LabelEditor.App.Views;
using LabelEditor.Application.Settings;
using LabelEditor.Domain.Entries;
using LabelEditor.Infrastructure.Storage;
using LabelEditor.Presentation.ViewModels;
using LabelEditor.Presentation.ViewModels.Properties;
using Microsoft.Extensions.DependencyInjection;

namespace LabelEditor.App.Tests;

/// <summary>Boots the real window with the real composition root, so XAML templates and bindings are exercised.</summary>
public class MainWindowTests
{
    [AvaloniaFact]
    public async Task Window_opens_a_label_and_shows_the_text_properties()
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
        using var services = App.ConfigureServices(paths, storage, window);
        var vm = services.GetRequiredService<MainViewModel>();
        window.DataContext = vm;
        window.Show();

        await vm.InitializeAsync(new EditorSettings { Language = "English" }, [Path.Combine(AppContext.BaseDirectory, "Fixtures", "Labels", "Plats128C_V3.xml")]);
        var document = Assert.Single(vm.Documents);
        var bold = document.Layout.Entries.OfType<TextEntry>().First(t => t.Font.IsBold);
        document.Session.Selection.Select(bold.Id);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var properties = Assert.IsType<TextPropertiesViewModel>(document.Properties);
        Assert.True(properties.Bold);
        Assert.NotEmpty(vm.FieldPicker.Nodes);
        Assert.Contains(vm.Library.Groups, g => g.Items.Count > 0);

        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var file = Path.Combine(AppContext.BaseDirectory, "rendered", "main-window.png");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        frame.Save(file);

        // The canvas context menu exists and its commands are bound to the document.
        var canvas = window.GetVisualDescendants().OfType<Controls.LabelCanvas>().Single();
        var menu = Assert.IsType<Avalonia.Controls.ContextMenu>(canvas.ContextMenu);
        menu.Open(canvas);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var items = menu.Items.OfType<Avalonia.Controls.MenuItem>().ToList();
        Assert.Contains(items, i => (i.Header as string) == "_Align");
        Assert.True(items.Single(i => (i.Header as string) == "_Duplicate").IsEffectivelyEnabled);
        window.CaptureRenderedFrame()?.Save(Path.Combine(AppContext.BaseDirectory, "rendered", "context-menu.png"));
        menu.Close();
        window.Close();
    }
}
