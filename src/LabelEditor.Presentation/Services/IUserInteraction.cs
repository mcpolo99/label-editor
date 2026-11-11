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

    /// <summary>Lets the user choose a data field from the field tree.</summary>
    /// <param name="resolver">Field catalog and captions to show.</param>
    /// <param name="currentFieldId">Field to select initially, if any.</param>
    /// <returns>The chosen field id, or null when cancelled.</returns>
    Task<int?> PickFieldAsync(Application.Preview.FieldTextResolver resolver, int? currentFieldId);
}

/// <summary>Installed font families, for the font picker.</summary>
public interface IFontCatalog
{
    IReadOnlyList<string> FontFamilies { get; }
}
