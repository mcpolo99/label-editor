namespace LabelEditor.Tests.Shared;

/// <summary>
/// A real LEdit installation for the tests that check the editor against Biesse's own files (stock labels,
/// the full field list, all language tables). Those files are not part of this repository, so these tests run
/// only where LEdit data is available and are skipped elsewhere (e.g. on the build server).
/// <para>
/// Looked up in this order: the <c>LABELEDITOR_LEDIT_DATA</c> environment variable (<c>none</c> = skip these tests),
/// <c>.temp/og-data/LEdit</c> in the repository (a local, git-ignored snapshot), then the standard installation folder.
/// </para>
/// </summary>
public static class RealLeditData
{
    public const string SkipReason = "Needs a real LEdit installation (set LABELEDITOR_LEDIT_DATA); Biesse's files are not in the repository.";

    public static string? Root { get; } = Find();

    public static string? LabelsFolder => Root is null ? null : Path.Combine(Root, "MixedData", "Label");

    private static string? Find()
    {
        var configured = Environment.GetEnvironmentVariable("LABELEDITOR_LEDIT_DATA");
        if (string.Equals(configured, "none", StringComparison.OrdinalIgnoreCase))
        {
            return null; // behave like the build server
        }

        var candidates = new List<string?> { configured };
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "LabelEditor.sln")))
            {
                candidates.Add(Path.Combine(dir.FullName, ".temp", "og-data", "LEdit"));
                break;
            }
        }

        candidates.Add(@"C:\BIESSE\BiesseWorks\Utils\LEdit");
        return candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c) && File.Exists(Path.Combine(c, "LEditTreeXML.xml")));
    }
}

/// <summary>A fact that needs <see cref="RealLeditData"/>; skipped when it is not available.</summary>
public sealed class RealLeditFactAttribute : FactAttribute
{
    public RealLeditFactAttribute()
    {
        if (RealLeditData.Root is null)
        {
            Skip = RealLeditData.SkipReason;
        }
    }
}

/// <summary>A theory that needs <see cref="RealLeditData"/>; skipped when it is not available.</summary>
public sealed class RealLeditTheoryAttribute : TheoryAttribute
{
    public RealLeditTheoryAttribute()
    {
        if (RealLeditData.Root is null)
        {
            Skip = RealLeditData.SkipReason;
        }
    }
}
