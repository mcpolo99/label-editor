using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using LabelEditor.App.Views;
using LabelEditor.Application.Settings;
using LabelEditor.Domain.Entries;
using LabelEditor.Infrastructure.Storage;
using LabelEditor.Presentation.ViewModels;
using LabelEditor.Presentation.ViewModels.Properties;
using Microsoft.Extensions.DependencyInjection;

namespace LabelEditor.App.Tests;

public class ComposerViewTests
{
    [AvaloniaFact]
    public async Task An_entry_with_several_fields_and_text_shows_one_chip_per_piece()
    {
        var root = Path.Combine(Path.GetTempPath(), "LabelEditorUiTests", Guid.NewGuid().ToString("N"));
        var window = new MainWindow { Width = 1400, Height = 860 };
        using var services = App.ConfigureServices(
            new AppPaths(AppContext.BaseDirectory, root, IsPortable: true),
            new StorageOptions { PackagedDefaultsRoot = Path.Combine(AppContext.BaseDirectory, "Defaults", "LEdit"), MachineInstallationRoot = null, WorkingFolder = root },
            window);
        var vm = services.GetRequiredService<MainViewModel>();
        window.DataContext = vm;
        window.Show();

        await vm.InitializeAsync(new EditorSettings { Language = "English" }, [Path.Combine(AppContext.BaseDirectory, "Fixtures", "Labels", "sample-multiple-fields.xml")]);
        var document = vm.ActiveDocument!;
        var entry = document.Layout.Entries.OfType<TextEntry>().Single();
        document.Session.Selection.Select(entry.Id);
        Dispatcher.UIThread.RunJobs();

        var panel = Assert.IsType<TextPropertiesViewModel>(document.Properties);
        Assert.Equal(["[Date]", "[QMin]", "[DrillInfo]", "\"sadada\""], panel.Elements.Select(e => e.Display));
        Assert.Equal("dsafdsfsfd3333te11ssadada", panel.Preview);

        var file = Path.Combine(AppContext.BaseDirectory, "rendered", "composer.png");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        window.CaptureRenderedFrame()?.Save(file);
        window.Close();
    }
}
