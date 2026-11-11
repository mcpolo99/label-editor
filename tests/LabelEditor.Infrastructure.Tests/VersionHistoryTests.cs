using LabelEditor.Domain.Layout;
using LabelEditor.Infrastructure.Ledit;
using LabelEditor.Infrastructure.Storage;

namespace LabelEditor.Infrastructure.Tests;

public sealed class VersionHistoryTests : IDisposable
{
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "LabelEditorTests", Guid.NewGuid().ToString("N"));
    private readonly FixedClock _clock = new(new DateTimeOffset(2026, 11, 11, 12, 0, 0, TimeSpan.Zero));
    private readonly FileLabelDocumentStore _store;

    public VersionHistoryTests()
    {
        Directory.CreateDirectory(_folder);
        _store = new FileLabelDocumentStore(new LeditLabelFormat(), _clock, "tester");
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private async Task<string> CopyFixtureAsync(string name)
    {
        var path = Path.Combine(_folder, name);
        await File.WriteAllBytesAsync(path, await File.ReadAllBytesAsync(TestData.Label(name)));
        return path;
    }

    [Fact]
    public async Task First_save_over_an_existing_label_keeps_the_original_as_version_1()
    {
        var path = await CopyFixtureAsync("Plats128C.xml");
        var original = await File.ReadAllBytesAsync(path);
        var layout = await _store.LoadAsync(path);

        await _store.SaveAsync(layout.WithLabelSize(90_000, 50_000), path);

        var versions = await _store.GetVersionsAsync(path);
        Assert.Equal([2, 1], versions.Select(v => v.Number));
        Assert.Equal(FileLabelDocumentStore.OriginalNote, versions[1].Note);
        Assert.Equal("tester", versions[0].SavedBy);
        Assert.Equal(_clock.Now, versions[0].SavedAt);
        var v1 = await _store.LoadVersionAsync(path, 1);
        Assert.Equal(TestData.WithoutIds(await new LeditLabelFormat().ReadAsync(new MemoryStream(original))), TestData.WithoutIds(v1));
        Assert.Equal(90_000, (await _store.LoadVersionAsync(path, 2)).Header.Shape.Width);
    }

    [Fact]
    public async Task Every_changing_save_adds_a_version_and_unchanged_saves_do_not()
    {
        var path = Path.Combine(_folder, "New.xml");
        var layout = LabelLayout.CreateNew();

        await _store.SaveAsync(layout, path);
        await _store.SaveAsync(layout, path); // same content
        _clock.Now = _clock.Now.AddMinutes(5);
        await _store.SaveAsync(layout.WithLabelSize(80_000, 40_000), path);

        var versions = await _store.GetVersionsAsync(path);
        Assert.Equal([2, 1], versions.Select(v => v.Number));
        Assert.Null(versions[1].Note); // a brand-new file has no "original"
        Assert.Equal(80_000, (await _store.LoadAsync(path)).Header.Shape.Width);
    }

    [Fact]
    public async Task The_label_file_keeps_its_clean_name_and_ledit_content()
    {
        var path = await CopyFixtureAsync("Plats128C.xml");
        var layout = await _store.LoadAsync(path);

        for (var i = 1; i <= 3; i++)
        {
            await _store.SaveAsync(layout.WithLabelSize(100_000 - (i * 1000), 60_000), path);
        }

        Assert.Equal(["Plats128C.xml", "Plats128C.xml" + EditorMetadataSidecar.Suffix], Directory.GetFiles(_folder).Select(Path.GetFileName).Order());
        Assert.Equal(4, (await _store.GetVersionsAsync(path)).Count);
        var bytes = await File.ReadAllBytesAsync(path);
        Assert.Equal(bytes, new LeditLabelFormat().Serialize(await _store.LoadVersionAsync(path, 4)));
    }

    [Fact]
    public async Task Versions_are_stored_compressed()
    {
        var path = await CopyFixtureAsync("Plats128C.xml");
        var layout = await _store.LoadAsync(path);
        for (var i = 0; i < 10; i++)
        {
            await _store.SaveAsync(layout.WithLabelSize(100_000 + i, 60_000), path);
        }

        var labelSize = new FileInfo(path).Length;
        var sidecarSize = new FileInfo(EditorMetadataSidecar.PathFor(path)).Length;
        Assert.True(sidecarSize < labelSize * 11 / 3, $"11 versions of a {labelSize} B label took {sidecarSize} B");
    }

    [Fact]
    public async Task A_label_without_history_has_no_versions()
    {
        var path = await CopyFixtureAsync("Plats128C.xml");

        Assert.Empty(await _store.GetVersionsAsync(path));
        await Assert.ThrowsAsync<Application.Abstractions.LabelDocumentException>(() => _store.LoadVersionAsync(path, 1));
    }
}
