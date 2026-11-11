using LabelEditor.Application.Abstractions;

namespace LabelEditor.Infrastructure.Storage;

/// <summary>Configuration for <see cref="StorageLocations"/>.</summary>
public sealed record StorageOptions
{
    /// <summary>Where an installed LEdit lives on Biesse machines.</summary>
    public const string DefaultMachineInstallation = @"C:\BIESSE\BiesseWorks\Utils\LEdit";

    /// <summary>Relative label folder inside an LEdit data root.</summary>
    public const string LabelSubfolder = @"MixedData\Label";

    /// <summary>
    /// Installed LEdit to read field, language and label data from. The editor ships none of LEdit's files; without
    /// an installation it starts with an empty field tree. Null disables the lookup.
    /// </summary>
    public string? MachineInstallationRoot { get; init; } = DefaultMachineInstallation;

    /// <summary>Writable folder for the user's labels.</summary>
    public required string WorkingFolder { get; init; }

    /// <summary>
    /// Allow saving into the installed LEdit's label folder. Off by default: production machines read
    /// those files and they are treated as read-only until deliberately enabled.
    /// </summary>
    public bool AllowWritingMachineLabels { get; init; }
}

/// <summary>
/// Resolves data and label folders. The installed LEdit is the only source of field, language and template data:
/// the editor does not ship copies of LEdit's files. Without an installation it still edits labels, with an empty
/// field tree, until the user points it at an LEdit folder.
/// </summary>
public sealed class StorageLocations : IStorageLocations
{
    private readonly StorageOptions _options;

    public StorageLocations(StorageOptions options)
    {
        _options = options;
        var machine = options.MachineInstallationRoot;
        DataRoot = machine is not null && LooksLikeLeditData(machine) ? Path.GetFullPath(machine) : null;
        WorkingFolder = Path.GetFullPath(options.WorkingFolder);

        var folders = new List<LabelFolder> { new(WorkingFolder, LabelSourceKind.Working, IsReadOnly: false) };
        if (DataRoot is not null)
        {
            folders.Add(new LabelFolder(Path.Combine(DataRoot, LabelSubfolderPath), LabelSourceKind.Machine, !options.AllowWritingMachineLabels));
        }

        LabelFolders = folders;
    }

    public string? DataRoot { get; }

    public bool IsMachineInstallation => DataRoot is not null;

    /// <summary>The LEdit folder that was looked for (shown when it was not found).</summary>
    public string? ExpectedInstallation => _options.MachineInstallationRoot;

    public IReadOnlyList<LabelFolder> LabelFolders { get; }

    public string WorkingFolder { get; }

    private static string LabelSubfolderPath => StorageOptions.LabelSubfolder.Replace('\\', Path.DirectorySeparatorChar);

    public bool IsWriteProtected(string path)
    {
        var full = Path.GetFullPath(path);
        if (DataRoot is null)
        {
            return false;
        }

        // The machine installation is read-only except, optionally, its label folder.
        var labelFolder = Path.Combine(DataRoot, LabelSubfolderPath);
        return !(_options.AllowWritingMachineLabels && IsUnder(full, labelFolder)) && IsUnder(full, DataRoot);
    }

    /// <summary>A folder counts as LEdit data when it has the field tree; that is what the editor needs from it.</summary>
    public static bool LooksLikeLeditData(string folder) =>
        Directory.Exists(folder) && File.Exists(Path.Combine(folder, "LEditTreeXML.xml"));

    private static bool IsUnder(string path, string folder)
    {
        var normalizedFolder = Path.TrimEndingDirectorySeparator(folder) + Path.DirectorySeparatorChar;
        return path.StartsWith(normalizedFolder, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(path, Path.TrimEndingDirectorySeparator(folder), StringComparison.OrdinalIgnoreCase);
    }
}
