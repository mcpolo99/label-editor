using LabelEditor.Application.Localization;
using LabelEditor.Application.Settings;
using LabelEditor.Domain.Fields;
using LabelEditor.Domain.Layout;

namespace LabelEditor.Application.Abstractions;

/// <summary>Reads and writes a label layout in a concrete file format (LEdit XML).</summary>
public interface ILabelLayoutFormat
{
    Task<LabelLayout> ReadAsync(Stream source, CancellationToken cancellationToken = default);

    Task WriteAsync(LabelLayout layout, Stream destination, CancellationToken cancellationToken = default);
}

/// <summary>Loads and saves label files on disk.</summary>
public interface ILabelDocumentStore
{
    Task<LabelLayout> LoadAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>Saves atomically: the target is only replaced once the new content is fully written.</summary>
    Task SaveAsync(LabelLayout layout, string path, CancellationToken cancellationToken = default);
}

/// <summary>Where the editor's reference data and labels live, and which locations must not be written.</summary>
public interface IStorageLocations
{
    /// <summary>
    /// Root of the installed LEdit whose field, language and label data the editor uses; null when none was found
    /// (the editor ships no LEdit files of its own).
    /// </summary>
    string? DataRoot { get; }

    /// <summary>True when an LEdit installation was found (<see cref="DataRoot"/> is set).</summary>
    bool IsMachineInstallation { get; }

    /// <summary>Folders to list labels from, in priority order.</summary>
    IReadOnlyList<LabelFolder> LabelFolders { get; }

    /// <summary>Default folder for "Save As" of new labels (always writable).</summary>
    string WorkingFolder { get; }

    bool IsWriteProtected(string path);
}

/// <summary>A folder that contains label layouts.</summary>
public sealed record LabelFolder(string Path, LabelSourceKind Kind, bool IsReadOnly);

/// <summary>Where a label in the library comes from.</summary>
public enum LabelSourceKind
{
    /// <summary>The user's own working folder.</summary>
    Working,

    /// <summary>The label folder of an installed LEdit (read only).</summary>
    Machine,

    /// <summary>Templates packaged with this editor (read only).</summary>
    Template,
}

/// <summary>A label file found in one of the <see cref="IStorageLocations.LabelFolders"/>.</summary>
public sealed record LabelLibraryItem(string Name, string Path, LabelSourceKind Source, bool IsReadOnly, DateTimeOffset LastModified);

/// <summary>Lists available label files.</summary>
public interface ILabelLibrary
{
    Task<IReadOnlyList<LabelLibraryItem>> GetItemsAsync(CancellationToken cancellationToken = default);
}

/// <summary>Supplies the NC data field tree.</summary>
public interface IFieldCatalogSource
{
    Task<FieldCatalog> LoadAsync(CancellationToken cancellationToken = default);
}

/// <summary>Supplies LEdit language tables.</summary>
public interface ILanguageSource
{
    Task<IReadOnlyList<LanguageInfo>> GetLanguagesAsync(CancellationToken cancellationToken = default);

    Task<TextCatalog> LoadAsync(string languageName, CancellationToken cancellationToken = default);
}

/// <summary>Persists user settings.</summary>
public interface ISettingsStore
{
    Task<EditorSettings> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(EditorSettings settings, CancellationToken cancellationToken = default);
}

/// <summary>A label file could not be read or written.</summary>
public sealed class LabelDocumentException : Exception
{
    public LabelDocumentException()
    {
    }

    public LabelDocumentException(string message)
        : base(message)
    {
    }

    public LabelDocumentException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
