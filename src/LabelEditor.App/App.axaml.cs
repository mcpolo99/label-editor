using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using LabelEditor.App.Services;
using LabelEditor.App.Views;
using LabelEditor.Application;
using LabelEditor.Infrastructure;
using LabelEditor.Infrastructure.Storage;
using LabelEditor.Presentation.Services;
using LabelEditor.Presentation.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LabelEditor.App;

/// <summary>Composition root. Wires every layer together; nothing else creates services.</summary>
public sealed class App : Avalonia.Application
{
    private ServiceProvider? _services;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            desktop.MainWindow = window;
            desktop.Exit += (_, _) => _services?.Dispose();

            // Show the window at once and do all I/O asynchronously behind it.
            Dispatcher.UIThread.Post(() => _ = StartAsync(window, desktop.Args ?? []));
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async Task StartAsync(MainWindow window, IReadOnlyList<string> args)
    {
        try
        {
            var paths = AppPaths.Detect();
            var settings = await new JsonSettingsStore(paths.SettingsFile, NullLogger<JsonSettingsStore>.Instance).LoadAsync();
            var storage = new StorageOptions
            {
                PackagedDefaultsRoot = paths.PackagedDefaultsRoot,
                MachineInstallationRoot = settings.LeditInstallationPath ?? StorageOptions.DefaultMachineInstallation,
                WorkingFolder = settings.WorkingFolder ?? paths.DefaultWorkingFolder,
            };

            _services = ConfigureServices(paths, storage, window);
            var viewModel = _services.GetRequiredService<MainViewModel>();
            window.DataContext = viewModel;
            await viewModel.InitializeAsync(settings, args.Where(File.Exists).ToList());
        }
        catch (Exception ex)
        {
            await new AvaloniaUserInteraction(window).ShowErrorAsync("Start-up failed", ex.ToString());
        }
    }

    internal static ServiceProvider ConfigureServices(AppPaths paths, StorageOptions storage, Window window)
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddDebug().SetMinimumLevel(LogLevel.Information));
        services.AddLabelEditorApplication();
        services.AddLabelEditorInfrastructure(paths, storage);
        services.AddSingleton<IUserInteraction>(new AvaloniaUserInteraction(window));
        services.AddSingleton<IFontCatalog, AvaloniaFontCatalog>();
        services.AddSingleton<EditorContext>();
        services.AddSingleton<MainViewModel>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }
}
