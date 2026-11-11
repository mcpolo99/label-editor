using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using LabelEditor.App.Views;
using LabelEditor.Application.Settings;
using LabelEditor.Domain.Entries;
using LabelEditor.Infrastructure.Storage;
using LabelEditor.Presentation.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace LabelEditor.App.Tests;

public class ThemeTests
{
    [AvaloniaFact]
    public async Task The_dark_theme_darkens_the_window_and_dims_the_label_paper()
    {
        var app = (App)Avalonia.Application.Current!;
        var root = Path.Combine(Path.GetTempPath(), "LabelEditorUiTests", Guid.NewGuid().ToString("N"));
        var window = new MainWindow { Width = 1400, Height = 860 };
        using var services = App.ConfigureServices(
            new AppPaths(AppContext.BaseDirectory, root, IsPortable: true),
            new StorageOptions { MachineInstallationRoot = Path.Combine(AppContext.BaseDirectory, "TestLEdit"), WorkingFolder = root },
            window);
        var vm = services.GetRequiredService<MainViewModel>();
        window.DataContext = vm;
        window.Show();
        try
        {
            await vm.InitializeAsync(new EditorSettings { Language = "English" }, [Path.Combine(AppContext.BaseDirectory, "Fixtures", "Labels", "sample-multiple-fields.xml")]);
            var document = vm.ActiveDocument!;
            document.Session.Selection.Select(document.Layout.Entries.OfType<TextEntry>().First().Id);

            vm.SetThemeCommand.Execute("Dark");
            app.ApplyTheme(vm.Theme); // what App does when the view model's Theme changes
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(AppTheme.Dark, vm.Theme);
            Assert.True(vm.IsDarkTheme);
            Assert.Equal(ThemeVariant.Dark, window.ActualThemeVariant);
            Assert.True(window.TryFindResource("ToolbarBackground", window.ActualThemeVariant, out var toolbar));
            Assert.True(((ISolidColorBrush)toolbar!).Color.R < 80, "toolbar should be dark");

            var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            var file = Path.Combine(AppContext.BaseDirectory, "rendered", "main-window-dark.png");
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            frame.Save(file);
        }
        finally
        {
            app.ApplyTheme(AppTheme.Light); // other tests render in the light theme
            window.Close();
        }
    }
}
