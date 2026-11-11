using LabelEditor.Domain.Entries;
using LabelEditor.Domain.Layout;
using LabelEditor.Infrastructure.Storage;
using LabelEditor.Tests.Shared;

namespace LabelEditor.Infrastructure.Tests;

internal static class TestData
{
    /// <summary>Labels that belong to this repository (written by the editor or by its authors).</summary>
    public static string LabelsFolder => Path.Combine(AppContext.BaseDirectory, "Fixtures", "Labels");

    /// <summary>The editor's own small LEdit-format data set (field tree, field list, two languages).</summary>
    public static string LeditRoot => Path.Combine(AppContext.BaseDirectory, "TestLEdit");

    /// <summary>A label from this repository, or else from the real LEdit installation (for <see cref="RealLeditFactAttribute"/> tests).</summary>
    public static string Label(string name)
    {
        var own = Path.Combine(LabelsFolder, name);
        return File.Exists(own) || RealLeditData.LabelsFolder is null ? own : Path.Combine(RealLeditData.LabelsFolder, name);
    }

    /// <summary>This repository's labels plus, where available, every label of the real LEdit installation.</summary>
    public static IEnumerable<string> AllLabels()
    {
        var own = Directory.EnumerateFiles(LabelsFolder, "*.xml").ToList();
        // LEdit's label folder also holds other XML (e.g. LabelLayoutsSources.xml): only label layouts count.
        var real = RealLeditData.LabelsFolder is { } folder && Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, "*.xml").Where(f => File.ReadAllText(f).Contains("<LabelLayout>", StringComparison.Ordinal))
            : [];
        var names = own.Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return own.Concat(real.Where(f => !names.Contains(Path.GetFileName(f)))).Order(StringComparer.OrdinalIgnoreCase);
    }

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

    /// <summary>Locations reading the test data set as "the LEdit installation".</summary>
    public static StorageLocations Locations(string? workingFolder = null, string? leditRoot = null) => new(new StorageOptions
    {
        MachineInstallationRoot = leditRoot ?? LeditRoot,
        WorkingFolder = workingFolder ?? Path.Combine(Path.GetTempPath(), "LabelEditorTests", Guid.NewGuid().ToString("N")),
    });

    /// <summary>Locations reading the real LEdit installation (only in <see cref="RealLeditFactAttribute"/> tests).</summary>
    public static StorageLocations RealLocations() => Locations(leditRoot: RealLeditData.Root);
}
