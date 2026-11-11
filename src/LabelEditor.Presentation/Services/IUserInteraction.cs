namespace LabelEditor.Presentation.Services;

/// <summary>Answer to "save changes?".</summary>
public enum SaveChangesAnswer
{
    Save,
    Discard,
    Cancel,
}

/// <summary>Answer to "replace the existing file?".</summary>
public enum ReplaceAnswer
{
    Replace,
    SaveAs,
    Cancel,
}

/// <summary>What to do with a version picked in the history dialog.</summary>
public enum HistoryAction
{
    Restore,
    OpenAsCopy,
}

/// <summary>The user's choice in the history dialog.</summary>
public sealed record HistoryChoice(int Number, HistoryAction Action);

/// <summary>Dialogs the view models need; implemented by the UI layer.</summary>
public interface IUserInteraction
{
    Task<string?> PickLabelToOpenAsync(string? initialFolder);

    Task<string?> PickLabelSaveLocationAsync(string suggestedName, string? initialFolder);

    Task<string?> PickBitmapAsync(string? initialFolder);

    Task<string?> PickPreviewDataAsync(string? initialFolder);

    Task<SaveChangesAnswer> AskSaveChangesAsync(string documentTitle);

    /// <summary>First save over an opened file. When <paramref name="protectedReason"/> is set, replacing is not offered.</summary>
    Task<ReplaceAnswer> AskReplaceExistingAsync(string path, string? protectedReason);

    Task ShowErrorAsync(string title, string message);

    Task ShowMessageAsync(string title, string message);

    /// <summary>Shows the saved versions of a label (newest first); null when closed without a choice.</summary>
    Task<HistoryChoice?> ShowHistoryAsync(string labelTitle, IReadOnlyList<Application.Abstractions.LabelVersionInfo> versions);

    /// <summary>Lets the user choose a data field from the field tree.</summary>
    /// <param name="resolver">Field catalog and captions to show.</param>
    /// <param name="currentFieldId">Field to select initially, if any.</param>
    /// <returns>The chosen field id, or null when cancelled.</returns>
    Task<int?> PickFieldAsync(Application.Preview.FieldTextResolver resolver, int? currentFieldId);
}

/// <summary>Which build is running and where it keeps its data (shown in the title bar and About).</summary>
/// <param name="Version">Product version, e.g. "0.2.0".</param>
/// <param name="Revision">Source revision it was built from (git commit), if known.</param>
/// <param name="Edition">"Installed", "Portable" or "Development build".</param>
/// <param name="ProgramFolder">Folder of the executable.</param>
/// <param name="UserDataFolder">Where settings are stored.</param>
/// <param name="WorkingFolder">Default folder for the user's labels.</param>
/// <param name="DataSource">The LEdit data in use (installation or packaged defaults).</param>
public sealed record ApplicationInfo(string Version, string? Revision, string Edition, string ProgramFolder, string UserDataFolder, string WorkingFolder, string DataSource);

/// <summary>Installed font families, for the font picker.</summary>
public interface IFontCatalog
{
    IReadOnlyList<string> FontFamilies { get; }
}
