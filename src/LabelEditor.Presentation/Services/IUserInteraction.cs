namespace LabelEditor.Presentation.Services;

/// <summary>Answer to "save changes?".</summary>
public enum SaveChangesAnswer
{
    Save,
    Discard,
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

    Task ShowErrorAsync(string title, string message);
}

/// <summary>Installed font families, for the font picker.</summary>
public interface IFontCatalog
{
    IReadOnlyList<string> FontFamilies { get; }
}
