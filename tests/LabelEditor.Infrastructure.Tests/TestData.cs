using LabelEditor.Domain.Entries;
using LabelEditor.Domain.Layout;
using LabelEditor.Infrastructure.Storage;

namespace LabelEditor.Infrastructure.Tests;

internal static class TestData
{
    public static string LabelsFolder => Path.Combine(AppContext.BaseDirectory, "Fixtures", "Labels");

    public static string DefaultsRoot => Path.Combine(AppContext.BaseDirectory, "Defaults", "LEdit");

    public static string Label(string name) => Path.Combine(LabelsFolder, name);

    public static IEnumerable<string> AllLabels() => Directory.EnumerateFiles(LabelsFolder, "*.xml").Order();

    public static TheoryData<string> AllLabelNames()
    {
        var data = new TheoryData<string>();
        foreach (var file in AllLabels())
        {
            data.Add(Path.GetFileName(file));
        }

        return data;
    }

    /// <summary>Entry ids are assigned on load, so compare layouts with ids blanked out.</summary>
    public static LabelLayout WithoutIds(LabelLayout layout) =>
        layout with { Entries = [.. layout.Entries.Select(e => e with { Id = default(EntryId) })] };

    public static StorageLocations DefaultsOnly(string? workingFolder = null) => new(new StorageOptions
    {
        PackagedDefaultsRoot = DefaultsRoot,
        MachineInstallationRoot = null,
        WorkingFolder = workingFolder ?? Path.Combine(Path.GetTempPath(), "LabelEditorTests", Guid.NewGuid().ToString("N")),
    });
}
