using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LabelEditor.Application;
using LabelEditor.Application.Abstractions;
using LabelEditor.Application.Documents;
using LabelEditor.Application.Editing;
using LabelEditor.Application.Localization;
using LabelEditor.Application.Preview;
using LabelEditor.Application.Settings;
using LabelEditor.Domain.Editing;
using LabelEditor.Domain.Entries;
using LabelEditor.Domain.Geometry;
using LabelEditor.Presentation.Services;
using LabelEditor.Presentation.ViewModels.Properties;
using Microsoft.Extensions.Logging;

namespace LabelEditor.Presentation.ViewModels;

/// <summary>A drawing tool on the toolbar.</summary>
public sealed partial class ToolOption(EditorTool tool, string name, string glyph, MainViewModel owner) : ObservableObject
{
    public EditorTool Tool { get; } = tool;

    public string Name { get; } = name;

    public string Glyph { get; } = glyph;

    [ObservableProperty]
    private bool _isSelected;

    partial void OnIsSelectedChanged(bool value)
    {
        if (value)
        {
            owner.Context.ActiveTool = Tool;
        }
    }
}

/// <summary>A file in the File ▸ Open recent menu.</summary>
public sealed record RecentFileItem(string Path, int Number)
{
    /// <summary>"_1  name.xml" (the number is the menu access key for the first nine).</summary>
    public string Header => (Number <= 9 ? $"_{Number}  " : $"{Number}  ") + System.IO.Path.GetFileName(Path).Replace("_", "__", StringComparison.Ordinal);

    public string? Folder => System.IO.Path.GetDirectoryName(Path);
}

/// <summary>Main window: open documents, shared panels and all menu commands.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly DocumentService _documents;
    private readonly ReferenceDataService _referenceData;
    private readonly ISettingsStore _settingsStore;
    private readonly IStorageLocations _locations;
    private readonly IUserInteraction _interaction;
    private readonly ILogger<MainViewModel> _logger;
    private readonly IReadOnlyList<IPreviewDataSource> _previewSources;
    private readonly ILabelVersionHistory _history;
    private readonly ApplicationInfo _app;
    private readonly ITextOverflowCheck _overflowCheck;
    private EditorSettings _settings = new();

    public MainViewModel(
        DocumentService documents,
        ReferenceDataService referenceData,
        ISettingsStore settingsStore,
        IStorageLocations locations,
        ILabelLibrary library,
        EditorContext context,
        IUserInteraction interaction,
        IEnumerable<IPreviewDataSource> previewSources,
        ILabelVersionHistory history,
        ApplicationInfo app,
        ITextOverflowCheck overflowCheck,
        ILogger<MainViewModel> logger)
    {
        _documents = documents;
        _referenceData = referenceData;
        _settingsStore = settingsStore;
        _locations = locations;
        _interaction = interaction;
        _logger = logger;
        _previewSources = previewSources.ToList();
        _history = history;
        _app = app;
        _overflowCheck = overflowCheck;
        Context = context;
        Library = new LabelLibraryViewModel(library, locations);
        Tools =
        [
            new(EditorTool.Select, "Select", "⬚", this),
            new(EditorTool.Text, "Text", "T", this),
            new(EditorTool.Barcode, "Bar code", "▥", this),
            new(EditorTool.QrCode, "QR code", "▦", this),
            new(EditorTool.Line, "Line", "╱", this),
            new(EditorTool.Ellipse, "Ellipse", "◯", this),
            new(EditorTool.Bitmap, "Bitmap", "▣", this),
            new(EditorTool.LabelGraphic, "Label graphic", "◩", this),
            new(EditorTool.CornerLogo, "Corner logo", "◰", this),
        ];
        Tools[0].IsSelected = true;
        Context.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(EditorContext.ActiveTool))
            {
                foreach (var tool in Tools)
                {
                    tool.IsSelected = tool.Tool == Context.ActiveTool;
                }
            }
        };
        Context.Clipboard.Changed += (_, _) => PasteCommand.NotifyCanExecuteChanged();
    }

    public EditorContext Context { get; }

    public LabelLibraryViewModel Library { get; }

    public FieldPickerViewModel FieldPicker { get; } = new();

    public IReadOnlyList<ToolOption> Tools { get; }

    public ObservableCollection<DocumentViewModel> Documents { get; } = [];

    public ObservableCollection<LanguageInfo> Languages { get; } = [];

    /// <summary>Recently opened or saved labels, newest first (File ▸ Open recent).</summary>
    public ObservableCollection<RecentFileItem> RecentFiles { get; } = [];

    public bool HasRecentFiles => RecentFiles.Count > 0;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand), nameof(SaveAsCommand), nameof(CloseDocumentCommand), nameof(ShowHistoryCommand))]
    private DocumentViewModel? _activeDocument;

    [ObservableProperty]
    private LanguageInfo? _selectedLanguage;

    [ObservableProperty]
    private string _status = "Loading…";

    [ObservableProperty]
    private bool _isBusy;

    public string DataSourceDescription => Library.DataSource;

    /// <summary>"Label Editor 0.2.0" (+ "portable"), so it is always visible which version is running.</summary>
    public string WindowTitle => $"Label Editor {_app.Version}" + (_app.Edition == "Portable" ? " (portable)" : string.Empty);

    [RelayCommand]
    private Task AboutAsync() => _interaction.ShowMessageAsync(
        "About Label Editor",
        $"Label Editor {_app.Version}{(_app.Revision is null ? string.Empty : $" (build {_app.Revision})")}\n" +
        $"{_app.Edition}\n\n" +
        $"Program:  {_app.ProgramFolder}\n" +
        $"Settings:  {_app.UserDataFolder}\n" +
        $"Labels:  {_app.WorkingFolder}\n" +
        $"LEdit data:  {_app.DataSource}\n\n" +
        "To update, run a newer LabelEditor-Setup and choose the same location; settings and labels are kept.");

    /// <summary>Loads settings-dependent state and reference data, then opens files given on the command line.</summary>
    public async Task InitializeAsync(EditorSettings settings, IReadOnlyList<string> filesToOpen)
    {
        _settings = settings;
        Context.SnapToGrid = settings.SnapToGrid;
        Context.ShowGrid = settings.ShowGrid;
        Context.DimLabelInDarkTheme = settings.DimLabelInDarkTheme;
        WarnTextOverflowOnSave = settings.WarnTextOverflowOnSave;
        _loadingTheme = true;
        Theme = settings.Theme;
        _loadingTheme = false;
        ShowRecentFiles(settings.RecentFiles);

        await RunBusyAsync("Loading fields and languages…", async () =>
        {
            var reference = await _referenceData.LoadAsync(settings.Language);
            ApplyReferenceData(reference);
            await Library.RefreshAsync();
        });

        foreach (var file in filesToOpen)
        {
            await OpenPathAsync(file);
        }

        if (Documents.Count == 0)
        {
            NewDocument();
        }

        Status = $"Ready. {DataSourceDescription}";
    }

    private void ApplyReferenceData(ReferenceData reference)
    {
        Context.Resolver = reference.CreateResolver();
        FieldPicker.Load(Context.Resolver);
        Languages.Clear();
        foreach (var language in reference.Languages)
        {
            Languages.Add(language);
        }

        _suppressLanguageChange = true;
        SelectedLanguage = Languages.FirstOrDefault(l => l.Name == reference.Texts.LanguageName);
        _suppressLanguageChange = false;
    }

    private bool _suppressLanguageChange;

    partial void OnSelectedLanguageChanged(LanguageInfo? value)
    {
        if (value is not null && !_suppressLanguageChange)
        {
            _ = ChangeLanguageAsync(value.Name);
        }
    }

    private async Task ChangeLanguageAsync(string language)
    {
        await RunBusyAsync($"Loading {language}…", async () =>
        {
            ApplyReferenceData(await _referenceData.LoadAsync(language));
            await SaveSettingsAsync(_settings with { Language = language });
        });
    }

    partial void OnActiveDocumentChanged(DocumentViewModel? oldValue, DocumentViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.Session.Changed -= OnActiveSessionChanged;
        }

        if (newValue is not null)
        {
            newValue.Session.Changed += OnActiveSessionChanged;
        }

        NotifyEditCommands();
    }

    private void OnActiveSessionChanged(object? sender, SessionChangedEventArgs e) => NotifyEditCommands();

    private void NotifyEditCommands()
    {
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
        CopyCommand.NotifyCanExecuteChanged();
        CutCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        DuplicateCommand.NotifyCanExecuteChanged();
        PasteCommand.NotifyCanExecuteChanged();
        ArrangeCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(IsSelectionSizeLocked));
        OnPropertyChanged(nameof(IsSelectionPositionLocked));
        OnPropertyChanged(nameof(UndoHeader));
        OnPropertyChanged(nameof(RedoHeader));
    }

    public bool IsSelectionSizeLocked => ActiveDocument?.IsSelectionSizeLocked ?? false;

    public bool IsSelectionPositionLocked => ActiveDocument?.IsSelectionPositionLocked ?? false;

    public string UndoHeader => ActiveDocument?.Session.History.UndoDescription is { } d ? $"_Undo {d}" : "_Undo";

    public string RedoHeader => ActiveDocument?.Session.History.RedoDescription is { } d ? $"_Redo {d}" : "_Redo";

    private EditorSession? Session => ActiveDocument?.Session;

    // ---------------------------------------------------------------- File

    [RelayCommand]
    private void NewDocument() => AddDocument(DocumentService.CreateNew());

    [RelayCommand]
    private async Task OpenAsync()
    {
        var folder = ActiveDocument?.FilePath is { } p ? Path.GetDirectoryName(p) : _locations.WorkingFolder;
        if (await _interaction.PickLabelToOpenAsync(folder) is { } path)
        {
            await OpenPathAsync(path);
        }
    }

    [RelayCommand]
    private Task OpenLibraryItemAsync(LabelLibraryItem? item) => item is null ? Task.CompletedTask : OpenPathAsync(item.Path);

    [RelayCommand]
    private async Task OpenTemplateAsCopyAsync(LabelLibraryItem? item)
    {
        if (item is null)
        {
            return;
        }

        await RunBusyAsync($"Opening {item.Name}…", async () => AddDocument(await _documents.OpenAsCopyAsync(item.Path)));
    }

    [RelayCommand]
    private async Task OpenRecentAsync(string? path)
    {
        if (path is null)
        {
            return;
        }

        if (!File.Exists(path))
        {
            await _interaction.ShowErrorAsync("Open recent", $"{path}\n\nThis file no longer exists (moved, renamed or deleted). It is removed from the list.");
            await SaveRecentFilesAsync(_settings.RecentFiles.Where(p => !string.Equals(p, path, StringComparison.OrdinalIgnoreCase)).ToList());
            return;
        }

        await OpenPathAsync(path);
    }

    [RelayCommand(CanExecute = nameof(HasRecentFiles))]
    private Task ClearRecentFilesAsync() => SaveRecentFilesAsync([]);

    public async Task OpenPathAsync(string path)
    {
        var existing = Documents.FirstOrDefault(d => string.Equals(d.FilePath, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            ActiveDocument = existing;
            await RememberRecentAsync(existing.FilePath!);
            return;
        }

        await RunBusyAsync($"Opening {Path.GetFileName(path)}…", async () =>
        {
            var session = await _documents.OpenAsync(path);
            ReplacePristineUntitled();
            AddDocument(session);
            await RememberRecentAsync(session.Location!.Path);
            Status = session.Location.IsReadOnly
                ? $"Opened {session.Title} (protected location: saving will ask for a new name)"
                : $"Opened {session.Title}";
        });
    }

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private async Task SaveAsync()
    {
        if (ActiveDocument is not { } document)
        {
            return;
        }

        await SaveDocumentAsync(document);
    }

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private async Task SaveAsAsync()
    {
        if (ActiveDocument is { } document && await ConfirmTextsFitAsync(document))
        {
            await SaveDocumentAsAsync(document);
        }
    }

    /// <summary>
    /// Saves in place. The first save over an opened file asks "replace existing?"; protected files
    /// (packaged templates, the machine's LEdit folder) can only be saved under a new name.
    /// </summary>
    /// <returns>False when the user cancelled or the save failed.</returns>
    public async Task<bool> SaveDocumentAsync(DocumentViewModel document)
    {
        if (!await ConfirmTextsFitAsync(document))
        {
            return false;
        }

        var session = document.Session;
        if (session.Location is not { } location)
        {
            return await SaveDocumentAsAsync(document);
        }

        if (!session.OverwriteConfirmed)
        {
            var reason = location.IsReadOnly ? ProtectedReason(location.Path) : null;
            switch (await _interaction.AskReplaceExistingAsync(location.Path, reason))
            {
                case ReplaceAnswer.Cancel:
                    return false;
                case ReplaceAnswer.SaveAs:
                    return await SaveDocumentAsAsync(document);
                default:
                    session.ConfirmOverwrite();
                    break;
            }
        }

        if (!DocumentService.CanSaveInPlace(session))
        {
            return await SaveDocumentAsAsync(document);
        }

        return await RunBusyAsync("Saving…", async () =>
        {
            await _documents.SaveAsync(session);
            Status = $"Saved {document.FilePath}";
        });
    }

    /// <summary>File ▸ Warn about text overflow: check texts against their fields before saving.</summary>
    [ObservableProperty]
    private bool _warnTextOverflowOnSave;

    partial void OnWarnTextOverflowOnSaveChanged(bool value)
    {
        if (value != _settings.WarnTextOverflowOnSave)
        {
            _ = SaveSettingsAsync(_settings with { WarnTextOverflowOnSave = value });
        }
    }

    /// <summary>
    /// Warns when texts are too wide for their field at their font size (with the sample values and every loaded
    /// preview record). Cancelling selects those texts so they can be fixed.
    /// </summary>
    private async Task<bool> ConfirmTextsFitAsync(DocumentViewModel document)
    {
        if (!WarnTextOverflowOnSave)
        {
            return true;
        }

        var overflows = _overflowCheck.Find(document.Layout, Context.Resolver, PreviewRecords);
        if (overflows.Count == 0)
        {
            return true;
        }

        const int shown = 8;
        var lines = overflows.Take(shown).Select(o => $"•  \"{o.Text}\"" + (o.RecordKey is null ? string.Empty : $"  (record {o.RecordKey})"));
        var more = overflows.Count > shown ? $"{Environment.NewLine}…and {overflows.Count - shown} more" : string.Empty;
        var subject = overflows.Count == 1 ? "1 text is too wide for its field at its font size" : $"{overflows.Count} texts are too wide for their fields at their font size";
        var message = $"{subject} and will not fit inside it on the label:{Environment.NewLine}{Environment.NewLine}"
            + string.Join(Environment.NewLine, lines) + more + Environment.NewLine + Environment.NewLine
            + "Save anyway? (Cancel selects them. The check can be turned off in the File menu; the canvas outlines these texts in orange.)";
        if (await _interaction.ConfirmAsync("Text does not fit", message, "Save anyway"))
        {
            return true;
        }

        document.Session.Selection.Select(overflows.Select(o => o.EntryId).ToList());
        Status = "Not saved: the selected texts do not fit their fields";
        return false;
    }

    private string ProtectedReason(string path) =>
        "This label is in the machine's LEdit folder, which is protected (enable AllowWritingMachineLabels in settings to change that).";

    private async Task<bool> SaveDocumentAsAsync(DocumentViewModel document)
    {
        var folder = document.FilePath is { } p && !document.IsReadOnly ? Path.GetDirectoryName(p) : _locations.WorkingFolder;
        Directory.CreateDirectory(_locations.WorkingFolder);
        var path = await _interaction.PickLabelSaveLocationAsync(document.Session.Title, folder);
        if (path is null)
        {
            return false;
        }

        return await RunBusyAsync("Saving…", async () =>
        {
            await _documents.SaveAsAsync(document.Session, path);
            await RememberRecentAsync(path);
            Status = $"Saved {path}";
            await Library.RefreshAsync();
        });
    }

    [RelayCommand(CanExecute = nameof(HasDocument))]
    private async Task CloseDocumentAsync(DocumentViewModel? document)
    {
        document ??= ActiveDocument;
        if (document is not null && await ConfirmCloseAsync(document))
        {
            RemoveDocument(document);
        }
    }

    /// <summary>Asks about unsaved changes in every document; false cancels closing the app.</summary>
    public async Task<bool> CanShutdownAsync()
    {
        foreach (var document in Documents.ToList())
        {
            ActiveDocument = document;
            if (!await ConfirmCloseAsync(document))
            {
                return false;
            }
        }

        await SaveSettingsAsync(_settings with { SnapToGrid = Context.SnapToGrid, ShowGrid = Context.ShowGrid, DimLabelInDarkTheme = Context.DimLabelInDarkTheme });
        return true;
    }

    /// <summary>Colour theme; the view applies it (System follows the Windows app mode).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSystemTheme), nameof(IsLightTheme), nameof(IsDarkTheme))]
    private AppTheme _theme = AppTheme.System;

    private bool _loadingTheme;

    public bool IsSystemTheme => Theme == AppTheme.System;

    public bool IsLightTheme => Theme == AppTheme.Light;

    public bool IsDarkTheme => Theme == AppTheme.Dark;

    /// <summary>
    /// File ▸ Choose LEdit folder: the editor reads fields, languages and labels from an installed LEdit only
    /// (it ships none of LEdit's files). The choice is saved and used from the next start.
    /// </summary>
    [RelayCommand]
    private async Task ChooseLeditFolderAsync()
    {
        if (await _interaction.PickFolderAsync("Choose the LEdit folder (with LEditTreeXML.xml)", _locations.DataRoot ?? _settings.LeditInstallationPath) is not { } folder)
        {
            return;
        }

        if (!File.Exists(Path.Combine(folder, "LEditTreeXML.xml")))
        {
            await _interaction.ShowMessageAsync("Not an LEdit folder", $"{folder}\n\ndoes not contain LEditTreeXML.xml. Choose the LEdit folder itself, e.g. C:\\BIESSE\\BiesseWorks\\Utils\\LEdit.");
            return;
        }

        await SaveSettingsAsync(_settings with { LeditInstallationPath = folder });
        await _interaction.ShowMessageAsync("LEdit folder", $"Label Editor will read fields, languages and labels from\n{folder}\n\nRestart Label Editor to use it.");
    }

    /// <summary>View ▸ Theme: "System", "Light" or "Dark".</summary>
    [RelayCommand]
    private void SetTheme(string? theme)
    {
        if (Enum.TryParse<AppTheme>(theme, ignoreCase: true, out var value))
        {
            Theme = value;
        }
    }

    partial void OnThemeChanged(AppTheme value)
    {
        if (!_loadingTheme)
        {
            _ = SaveSettingsAsync(_settings with { Theme = value });
        }
    }

    private async Task<bool> ConfirmCloseAsync(DocumentViewModel document)
    {
        if (!document.Session.IsDirty)
        {
            return true;
        }

        return await _interaction.AskSaveChangesAsync(document.Session.Title) switch
        {
            SaveChangesAnswer.Save => await SaveDocumentAsync(document),
            SaveChangesAnswer.Discard => true,
            _ => false,
        };
    }

    private bool HasDocument() => ActiveDocument is not null;

    /// <summary>
    /// Shows the label's saved versions. Restore brings one back as an undoable edit (save to make it current);
    /// Open as copy opens it in a new, untitled tab.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasDocument))]
    private async Task ShowHistoryAsync()
    {
        if (ActiveDocument is not { } document)
        {
            return;
        }

        if (document.FilePath is not { } path)
        {
            await _interaction.ShowErrorAsync("History", "This label has not been saved yet. A version is recorded every time it is saved.");
            return;
        }

        var versions = await _history.GetVersionsAsync(path);
        if (await _interaction.ShowHistoryAsync(document.Session.Title, versions) is not { } choice)
        {
            return;
        }

        await RunBusyAsync($"Loading version {choice.Number}…", async () =>
        {
            var layout = await _history.LoadVersionAsync(path, choice.Number);
            if (choice.Action == HistoryAction.Restore)
            {
                document.Session.Execute($"Restore version {choice.Number}", _ => layout, []);
                Status = $"Restored version {choice.Number} of {document.Session.Title}. Save to make it the current version.";
            }
            else
            {
                AddDocument(new EditorSession(layout));
                Status = $"Opened version {choice.Number} of {document.Session.Title} as a new label.";
            }
        });
    }

    // ---------------------------------------------------------------- Edit / Arrange
    // These act on the active document; the logic lives in DocumentViewModel (shared with the canvas context menu).

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo() => ActiveDocument?.UndoCommand.Execute(null);

    private bool CanUndo() => ActiveDocument?.UndoCommand.CanExecute(null) ?? false;

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo() => ActiveDocument?.RedoCommand.Execute(null);

    private bool CanRedo() => ActiveDocument?.RedoCommand.CanExecute(null) ?? false;

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Cut() => ActiveDocument?.CutCommand.Execute(null);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Copy() => ActiveDocument?.CopyCommand.Execute(null);

    [RelayCommand(CanExecute = nameof(CanPaste))]
    private void Paste() => ActiveDocument?.PasteCommand.Execute(null);

    private bool CanPaste() => ActiveDocument?.PasteCommand.CanExecute(null) ?? false;

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Delete() => ActiveDocument?.DeleteCommand.Execute(null);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Duplicate() => ActiveDocument?.DuplicateCommand.Execute(null);

    [RelayCommand]
    private void SelectAll() => ActiveDocument?.SelectAllCommand.Execute(null);

    private bool HasSelection() => ActiveDocument?.CutCommand.CanExecute(null) ?? false;

    /// <inheritdoc cref="DocumentViewModel.ArrangeCommand"/>
    [RelayCommand(CanExecute = nameof(CanArrange))]
    private void Arrange(string? operation) => ActiveDocument?.ArrangeCommand.Execute(operation);

    private bool CanArrange(string? operation) => ActiveDocument?.CanArrange(operation) ?? false;

    // ---------------------------------------------------------------- View

    [RelayCommand]
    private void ZoomIn() => SetZoom(z => z * 1.25);

    [RelayCommand]
    private void ZoomOut() => SetZoom(z => z / 1.25);

    [RelayCommand]
    private void ZoomActualSize() => SetZoom(_ => 1);

    private void SetZoom(Func<double, double> change)
    {
        if (ActiveDocument is { } d)
        {
            d.Zoom = Math.Clamp(change(d.Zoom), 0.25, 20);
        }
    }

    [RelayCommand]
    private void SelectTool(EditorTool tool) => Context.ActiveTool = tool;

    // ---------------------------------------------------------------- Fields

    /// <summary>Inserts a field: appended to the selected text entry, or as a new text entry.</summary>
    [RelayCommand]
    private void InsertField(FieldNodeViewModel? node)
    {
        if (node?.FieldId is not { } id || ActiveDocument is not { } document)
        {
            return;
        }

        document.InsertField(id, null);
    }

    // ---------------------------------------------------------------- Preview data

    public ObservableCollection<PreviewRecord> PreviewRecords { get; } = [];

    [ObservableProperty]
    private string? _previewDataDescription;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextRecordCommand), nameof(PreviousRecordCommand))]
    private PreviewRecord? _selectedPreviewRecord;

    partial void OnSelectedPreviewRecordChanged(PreviewRecord? value) => Context.PreviewRecord = value;

    [RelayCommand]
    private async Task LoadPreviewDataAsync()
    {
        if (await _interaction.PickPreviewDataAsync(_locations.WorkingFolder) is not { } path)
        {
            return;
        }

        await RunBusyAsync($"Loading {Path.GetFileName(path)}…", async () =>
        {
            var source = _previewSources.FirstOrDefault(s => s.CanRead(path))
                ?? throw new LabelDocumentException($"'{Path.GetFileName(path)}' is not a supported data file (label sheet *_lbl.xml).");
            var data = await source.LoadAsync(path);
            PreviewRecords.Clear();
            foreach (var record in data.Records)
            {
                PreviewRecords.Add(record);
            }

            PreviewDataDescription = data.Description;
            SelectedPreviewRecord = PreviewRecords.FirstOrDefault();
            Status = $"Previewing {data.Description}";
        });
    }

    [RelayCommand]
    private void ClearPreviewData()
    {
        PreviewRecords.Clear();
        PreviewDataDescription = null;
        SelectedPreviewRecord = null;
    }

    [RelayCommand(CanExecute = nameof(HasNextRecord))]
    private void NextRecord() => StepRecord(1);

    [RelayCommand(CanExecute = nameof(HasPreviousRecord))]
    private void PreviousRecord() => StepRecord(-1);

    private bool HasNextRecord() => SelectedPreviewRecord is { } r && PreviewRecords.IndexOf(r) < PreviewRecords.Count - 1;

    private bool HasPreviousRecord() => SelectedPreviewRecord is { } r && PreviewRecords.IndexOf(r) > 0;

    private void StepRecord(int delta)
    {
        if (SelectedPreviewRecord is { } r)
        {
            SelectedPreviewRecord = PreviewRecords[Math.Clamp(PreviewRecords.IndexOf(r) + delta, 0, PreviewRecords.Count - 1)];
        }
    }

    // ---------------------------------------------------------------- helpers

    private void AddDocument(EditorSession session)
    {
        var document = new DocumentViewModel(session, Context, _interaction) { Zoom = _settings.Zoom };
        Documents.Add(document);
        ActiveDocument = document;
    }

    private void RemoveDocument(DocumentViewModel document)
    {
        var index = Documents.IndexOf(document);
        Documents.Remove(document);
        document.Dispose();
        ActiveDocument = Documents.Count == 0 ? null : Documents[Math.Clamp(index, 0, Documents.Count - 1)];
    }

    /// <summary>Opening a file replaces the empty start-up document instead of piling up tabs.</summary>
    private void ReplacePristineUntitled()
    {
        if (Documents is [{ FilePath: null, Session: { IsDirty: false, History.CanUndo: false } } only])
        {
            RemoveDocument(only);
        }
    }

    private Task RememberRecentAsync(string path) => SaveRecentFilesAsync(_settings.WithRecentFile(path).RecentFiles);

    private async Task SaveRecentFilesAsync(IReadOnlyList<string> files)
    {
        ShowRecentFiles(files);
        await SaveSettingsAsync(_settings with { RecentFiles = files });
    }

    private void ShowRecentFiles(IReadOnlyList<string> files)
    {
        RecentFiles.Clear();
        foreach (var (file, index) in files.Select((f, i) => (f, i)))
        {
            RecentFiles.Add(new RecentFileItem(file, index + 1));
        }

        OnPropertyChanged(nameof(HasRecentFiles));
        ClearRecentFilesCommand.NotifyCanExecuteChanged();
    }

    private async Task SaveSettingsAsync(EditorSettings settings)
    {
        _settings = settings;
        try
        {
            await _settingsStore.SaveAsync(settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not save settings");
        }
    }

    /// <summary>Runs an async operation with a status message; errors are shown to the user, not thrown.</summary>
    private async Task<bool> RunBusyAsync(string message, Func<Task> operation)
    {
        IsBusy = true;
        Status = message;
        try
        {
            await operation();
            return true;
        }
        catch (Exception ex) when (ex is LabelDocumentException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _logger.LogError(ex, "{Operation} failed", message);
            Status = ex.Message;
            await _interaction.ShowErrorAsync(message.TrimEnd('…'), ex.Message);
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
