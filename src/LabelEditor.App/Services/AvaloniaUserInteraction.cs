using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using LabelEditor.Presentation.Services;

namespace LabelEditor.App.Services;

/// <summary>File pickers and message boxes for the view models.</summary>
public sealed class AvaloniaUserInteraction(Window owner) : IUserInteraction
{
    private static readonly FilePickerFileType LabelFiles = new("LEdit label layout") { Patterns = ["*.xml"] };
    private static readonly FilePickerFileType LabelSheets = new("Label sheet") { Patterns = ["*_lbl.xml", "*.xml"] };
    private static readonly FilePickerFileType Bitmaps = new("Bitmap") { Patterns = ["*.bmp", "*.png", "*.jpg"] };

    public async Task<string?> PickLabelToOpenAsync(string? initialFolder) => await PickOneAsync("Open label", LabelFiles, initialFolder);

    public async Task<string?> PickBitmapAsync(string? initialFolder) => await PickOneAsync("Choose bitmap", Bitmaps, initialFolder);

    public async Task<string?> PickPreviewDataAsync(string? initialFolder) => await PickOneAsync("Open preview data", LabelSheets, initialFolder);

    public async Task<string?> PickLabelSaveLocationAsync(string suggestedName, string? initialFolder)
    {
        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save label as",
            SuggestedFileName = suggestedName,
            DefaultExtension = "xml",
            FileTypeChoices = [LabelFiles],
            ShowOverwritePrompt = true,
            SuggestedStartLocation = await FolderAsync(initialFolder),
        });
        return file?.TryGetLocalPath();
    }

    public async Task<SaveChangesAnswer> AskSaveChangesAsync(string documentTitle)
    {
        var answer = await ShowDialogAsync("Unsaved changes", $"Save changes to \"{documentTitle}\"?", ["Save", "Don't save", "Cancel"]);
        return answer switch
        {
            0 => SaveChangesAnswer.Save,
            1 => SaveChangesAnswer.Discard,
            _ => SaveChangesAnswer.Cancel,
        };
    }

    public async Task ShowErrorAsync(string title, string message) => await ShowDialogAsync(title, message, ["OK"]);

    private async Task<string?> PickOneAsync(string title, FilePickerFileType type, string? initialFolder)
    {
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [type, FilePickerFileTypes.All],
            SuggestedStartLocation = await FolderAsync(initialFolder),
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    private async Task<IStorageFolder?> FolderAsync(string? path) =>
        path is not null && Directory.Exists(path) ? await owner.StorageProvider.TryGetFolderFromPathAsync(path) : null;

    /// <returns>Index of the chosen button; the last button when closed.</returns>
    private async Task<int> ShowDialogAsync(string title, string message, IReadOnlyList<string> buttons)
    {
        var result = buttons.Count - 1;
        var dialog = new Window
        {
            Title = title,
            SizeToContent = SizeToContent.WidthAndHeight,
            MaxWidth = 640,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        for (var i = 0; i < buttons.Count; i++)
        {
            var index = i;
            var button = new Button { Content = buttons[i], MinWidth = 80, IsDefault = i == 0, IsCancel = i == buttons.Count - 1 };
            button.Click += (_, _) =>
            {
                result = index;
                dialog.Close();
            };
            row.Children.Add(button);
        }

        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Spacing = 16,
            Children =
            {
                new SelectableTextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 600 },
                row,
            },
        };

        if (owner.IsVisible)
        {
            await dialog.ShowDialog(owner);
        }

        return result;
    }
}

/// <summary>Installed font families from the platform.</summary>
public sealed class AvaloniaFontCatalog : IFontCatalog
{
    private readonly Lazy<IReadOnlyList<string>> _families = new(() =>
        FontManager.Current.SystemFonts.Select(f => f.Name).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.CurrentCultureIgnoreCase).ToList());

    public IReadOnlyList<string> FontFamilies => _families.Value;
}
