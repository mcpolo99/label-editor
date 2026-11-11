using LabelEditor.Application.Abstractions;
using LabelEditor.Application.Documents;
using LabelEditor.Infrastructure.Ledit;
using LabelEditor.Infrastructure.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace LabelEditor.Infrastructure.Tests;

public sealed class StorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "LabelEditorTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private StorageLocations FakeMachine(bool allowWritingLabels = false)
    {
        var machine = Path.Combine(_root, "machine", "LEdit");
        Directory.CreateDirectory(Path.Combine(machine, "MixedData", "Label"));
        File.Copy(Path.Combine(TestData.LeditRoot, "LEditTreeXML.xml"), Path.Combine(machine, "LEditTreeXML.xml"));
        File.Copy(TestData.Label("test-label.xml"), Path.Combine(machine, "MixedData", "Label", "test-label.xml"));
        return new StorageLocations(new StorageOptions
        {
            MachineInstallationRoot = machine,
            WorkingFolder = Path.Combine(_root, "work"),
            AllowWritingMachineLabels = allowWritingLabels,
        });
    }

    [Fact]
    public void Prefers_an_installed_ledit_and_protects_it()
    {
        var locations = FakeMachine();

        Assert.True(locations.IsMachineInstallation);
        Assert.EndsWith(Path.Combine("machine", "LEdit"), locations.DataRoot!, StringComparison.Ordinal);
        Assert.True(locations.IsWriteProtected(Path.Combine(locations.DataRoot!, "MixedData", "Label", "test-label.xml")));
        Assert.False(locations.IsWriteProtected(Path.Combine(locations.WorkingFolder, "Mine.xml")));
    }

    [Fact]
    public void Machine_label_folder_can_be_unlocked_explicitly()
    {
        var locations = FakeMachine(allowWritingLabels: true);

        Assert.False(locations.IsWriteProtected(Path.Combine(locations.DataRoot!, "MixedData", "Label", "test-label.xml")));
        Assert.True(locations.IsWriteProtected(Path.Combine(locations.DataRoot!, "LEditTreeXML.xml")));
    }

    [Fact]
    public async Task Without_an_installation_there_is_no_ledit_data_and_only_the_working_folder()
    {
        // The editor ships none of LEdit's files: no installation means an empty field tree, not bundled copies.
        var locations = new StorageLocations(new StorageOptions
        {
            MachineInstallationRoot = Path.Combine(_root, "does-not-exist"),
            WorkingFolder = Path.Combine(_root, "work"),
        });

        Assert.False(locations.IsMachineInstallation);
        Assert.Null(locations.DataRoot);
        Assert.Equal([LabelSourceKind.Working], locations.LabelFolders.Select(f => f.Kind));
        Assert.False(locations.IsWriteProtected(Path.Combine(_root, "anywhere", "a.xml")));
        var catalog = await new LeditFieldCatalogSource(locations, NullLogger<LeditFieldCatalogSource>.Instance).LoadAsync();
        Assert.Empty(catalog.Fields);
        Assert.Empty(await new LeditLanguageSource(locations, NullLogger<LeditLanguageSource>.Instance).GetLanguagesAsync());
    }

    [Fact]
    public async Task Library_lists_labels_from_all_folders_with_their_source()
    {
        var locations = FakeMachine();
        var library = new FileSystemLabelLibrary(locations, NullLogger<FileSystemLabelLibrary>.Instance);

        var items = await library.GetItemsAsync();

        Assert.Contains(items, i => i is { Name: "test-label", Source: LabelSourceKind.Machine, IsReadOnly: true });
    }

    [Fact]
    public async Task Save_as_into_a_protected_folder_is_refused_and_nothing_is_written()
    {
        var locations = FakeMachine();
        var service = new DocumentService(new FileLabelDocumentStore(new LeditLabelFormat()), locations);
        var session = await service.OpenAsync(Path.Combine(locations.DataRoot!, "MixedData", "Label", "test-label.xml"));
        var before = await File.ReadAllBytesAsync(session.Location!.Path);

        Assert.True(session.Location.IsReadOnly);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync(session));
        await Assert.ThrowsAsync<LabelDocumentException>(() => service.SaveAsAsync(session, session.Location.Path));
        Assert.Equal(before, await File.ReadAllBytesAsync(session.Location.Path));
    }

    [Fact]
    public async Task Save_writes_atomically_and_clears_dirty_state()
    {
        var locations = FakeMachine();
        var service = new DocumentService(new FileLabelDocumentStore(new LeditLabelFormat()), locations);
        var session = DocumentService.CreateNew();
        session.Execute("resize", l => l.WithLabelSize(80_000, 40_000));
        var target = Path.Combine(locations.WorkingFolder, "New.xml");

        await service.SaveAsAsync(session, target);

        Assert.False(session.IsDirty);
        Assert.Equal(target, session.Location?.Path);
        Assert.DoesNotContain(Directory.GetFiles(locations.WorkingFolder), f => f.EndsWith(".tmp", StringComparison.Ordinal)); // no temp file left behind
        var reloaded = await new FileLabelDocumentStore(new LeditLabelFormat()).LoadAsync(target);
        Assert.Equal(80_000, reloaded.Header.Shape.Width);
    }

    [Fact]
    public async Task Settings_survive_a_round_trip_and_corrupt_files_yield_defaults()
    {
        var file = Path.Combine(_root, "settings.json");
        var store = new JsonSettingsStore(file, NullLogger<JsonSettingsStore>.Instance);
        await store.SaveAsync(new Application.Settings.EditorSettings { Language = "Svenska" }.WithRecentFile(@"C:\a.xml"));

        var loaded = await store.LoadAsync();
        Assert.Equal("Svenska", loaded.Language);
        Assert.Equal([@"C:\a.xml"], loaded.RecentFiles);

        await store.SaveAsync(loaded with { Theme = Application.Settings.AppTheme.Dark, DimLabelInDarkTheme = false });
        Assert.Contains("\"Theme\": \"Dark\"", await File.ReadAllTextAsync(file)); // readable in the file
        var themed = await store.LoadAsync();
        Assert.Equal(Application.Settings.AppTheme.Dark, themed.Theme);
        Assert.False(themed.DimLabelInDarkTheme);

        await File.WriteAllTextAsync(file, "{ not json");
        Assert.Null((await store.LoadAsync()).Language);
    }

    [Fact]
    public void Portable_mode_keeps_user_data_next_to_the_executable()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, AppPaths.PortableMarkerFileName), string.Empty);

        var paths = AppPaths.Detect(_root);

        Assert.True(paths.IsPortable);
        Assert.StartsWith(_root, paths.SettingsFile, StringComparison.Ordinal);
        Assert.StartsWith(_root, paths.DefaultWorkingFolder, StringComparison.Ordinal);
    }
}
