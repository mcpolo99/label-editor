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
        File.Copy(Path.Combine(TestData.DefaultsRoot, "LEditTreeXML.xml"), Path.Combine(machine, "LEditTreeXML.xml"));
        File.Copy(TestData.Label("Plats128C.xml"), Path.Combine(machine, "MixedData", "Label", "Plats128C.xml"));
        return new StorageLocations(new StorageOptions
        {
            PackagedDefaultsRoot = TestData.DefaultsRoot,
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
        Assert.EndsWith(Path.Combine("machine", "LEdit"), locations.DataRoot, StringComparison.Ordinal);
        Assert.True(locations.IsWriteProtected(Path.Combine(locations.DataRoot, "MixedData", "Label", "Plats128C.xml")));
        Assert.True(locations.IsWriteProtected(Path.Combine(TestData.DefaultsRoot, "MixedData", "Label", "LayoutDef.xml")));
        Assert.False(locations.IsWriteProtected(Path.Combine(locations.WorkingFolder, "Mine.xml")));
    }

    [Fact]
    public void Machine_label_folder_can_be_unlocked_explicitly()
    {
        var locations = FakeMachine(allowWritingLabels: true);

        Assert.False(locations.IsWriteProtected(Path.Combine(locations.DataRoot, "MixedData", "Label", "Plats128C.xml")));
        Assert.True(locations.IsWriteProtected(Path.Combine(locations.DataRoot, "LEditTreeXML.xml")));
    }

    [Fact]
    public void Falls_back_to_packaged_defaults_without_an_installation()
    {
        var locations = new StorageLocations(new StorageOptions
        {
            PackagedDefaultsRoot = TestData.DefaultsRoot,
            MachineInstallationRoot = Path.Combine(_root, "does-not-exist"),
            WorkingFolder = Path.Combine(_root, "work"),
        });

        Assert.False(locations.IsMachineInstallation);
        Assert.Equal(Path.GetFullPath(TestData.DefaultsRoot), locations.DataRoot);
    }

    [Fact]
    public async Task Library_lists_labels_from_all_folders_with_their_source()
    {
        var locations = FakeMachine();
        var library = new FileSystemLabelLibrary(locations, NullLogger<FileSystemLabelLibrary>.Instance);

        var items = await library.GetItemsAsync();

        Assert.Contains(items, i => i is { Name: "Plats128C", Source: LabelSourceKind.Machine, IsReadOnly: true });
        Assert.Contains(items, i => i is { Name: "LayoutDef", Source: LabelSourceKind.Template, IsReadOnly: true });
    }

    [Fact]
    public async Task Save_as_into_a_protected_folder_is_refused_and_nothing_is_written()
    {
        var locations = FakeMachine();
        var service = new DocumentService(new FileLabelDocumentStore(new LeditLabelFormat()), locations);
        var session = await service.OpenAsync(Path.Combine(locations.DataRoot, "MixedData", "Label", "Plats128C.xml"));
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
        Assert.Single(Directory.GetFiles(locations.WorkingFolder)); // no temp file left behind
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
