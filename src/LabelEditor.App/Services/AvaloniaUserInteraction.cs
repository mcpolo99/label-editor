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

    public async Task<ReplaceAnswer> AskReplaceExistingAsync(string path, string? protectedReason)
    {
        if (protectedReason is not null)
        {
            var choice = await ShowDialogAsync("Save label", $"{protectedReason}\n\n{path}\n\nSave your changes as a new label?", ["Save As…", "Cancel"]);
            return choice == 0 ? ReplaceAnswer.SaveAs : ReplaceAnswer.Cancel;
        }

        var answer = await ShowDialogAsync("Replace existing label", $"Replace the existing file?\n\n{path}", ["Replace", "Save As…", "Cancel"]);
        return answer switch
        {
            0 => ReplaceAnswer.Replace,
            1 => ReplaceAnswer.SaveAs,
            _ => ReplaceAnswer.Cancel,
        };
    }

    public async Task ShowErrorAsync(string title, string message) => await ShowDialogAsync(title, message, ["OK"]);

    public async Task ShowMessageAsync(string title, string message) => await ShowDialogAsync(title, message, ["OK"]);

    public async Task<HistoryChoice?> ShowHistoryAsync(string labelTitle, IReadOnlyList<Application.Abstractions.LabelVersionInfo> versions)
    {
        HistoryChoice? result = null;
        var dialog = new Window
        {
            Title = $"History – {labelTitle}",
            Width = 560,
            Height = 420,
            MinWidth = 400,
            MinHeight = 260,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Icon = owner.Icon,
        };

        var list = new ListBox
        {
            ItemsSource = versions,
            SelectedIndex = versions.Count > 0 ? 0 : -1,
            ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<Application.Abstractions.LabelVersionInfo>((v, _) => new TextBlock
            {
                Text = v is null
                    ? string.Empty
                    : $"Version {v.Number}    {v.SavedAt.ToLocalTime():yyyy-MM-dd HH:mm}    {v.SavedBy}    {v.SizeBytes / 1024.0:0.#} KB{(v.Note is null ? string.Empty : "    " + v.Note)}",
            }),
        };

        void Choose(HistoryAction action)
        {
            if (list.SelectedItem is Application.Abstractions.LabelVersionInfo version)
            {
                result = new HistoryChoice(version.Number, action);
                dialog.Close();
            }
        }

        var restore = new Button { Content = "Restore", IsDefault = true, MinWidth = 90 };
        restore.Click += (_, _) => Choose(HistoryAction.Restore);
        var copy = new Button { Content = "Open as copy", MinWidth = 110 };
        copy.Click += (_, _) => Choose(HistoryAction.OpenAsCopy);
        var close = new Button { Content = "Close", IsCancel = true, MinWidth = 80 };
        close.Click += (_, _) => dialog.Close();
        list.DoubleTapped += (_, _) => Choose(HistoryAction.OpenAsCopy);

        var hint = new TextBlock
        {
            Text = versions.Count == 0
                ? "No versions yet. A version is recorded every time the label is saved."
                : "Restore brings the version back into the editor (save to make it current). Open as copy opens it in a new tab.",
            TextWrapping = TextWrapping.Wrap,
        };
        hint.Bind(TextBlock.ForegroundProperty, hint.GetResourceObservable("HintForeground")); // follows the theme
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right, Children = { restore, copy, close } };
        var panel = new DockPanel { Margin = new Avalonia.Thickness(10) };
        DockPanel.SetDock(hint, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        hint.Margin = new Avalonia.Thickness(0, 0, 0, 8);
        buttons.Margin = new Avalonia.Thickness(0, 8, 0, 0);
        panel.Children.Add(hint);
        panel.Children.Add(buttons);
        panel.Children.Add(list);
        dialog.Content = panel;

        await dialog.ShowDialog(owner);
        return result;
    }

    public async Task<int?> PickFieldAsync(Application.Preview.FieldTextResolver resolver, int? currentFieldId)
    {
        var picker = new Presentation.ViewModels.FieldPickerViewModel();
        picker.Load(resolver);
        if (currentFieldId is { } id)
        {
            picker.Reveal(id);
        }

        var chosen = await new Views.FieldPickerDialog { DataContext = picker }.ShowDialog<IReadOnlyList<int>?>(owner);
        return chosen is { Count: > 0 } ? chosen[0] : null;
    }

    public async Task<IReadOnlyList<int>> PickFieldsAsync(Application.Preview.FieldTextResolver resolver)
    {
        var picker = new Presentation.ViewModels.FieldPickerViewModel();
        picker.Load(resolver);
        return await new Views.FieldPickerDialog { DataContext = picker, AllowMultiple = true }.ShowDialog<IReadOnlyList<int>?>(owner) ?? [];
    }

    public async Task<string?> PickFolderAsync(string title, string? initialFolder)
    {
        var folders = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            SuggestedStartLocation = await FolderAsync(initialFolder),
        });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

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
