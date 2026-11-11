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

    public ObservableCollection<string> RecentFiles { get; } = [];

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

    /// <summary>Loads settings-dependent state and reference data, then opens files given on the command line.</summary>
    public async Task InitializeAsync(EditorSettings settings, IReadOnlyList<string> filesToOpen)
    {
        _settings = settings;
        Context.SnapToGrid = settings.SnapToGrid;
        Context.ShowGrid = settings.ShowGrid;
        foreach (var file in settings.RecentFiles)
        {
            RecentFiles.Add(file);
        }

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
        OnPropertyChanged(nameof(UndoHeader));
        OnPropertyChanged(nameof(RedoHeader));
    }

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
    private Task OpenRecentAsync(string? path) => path is null ? Task.CompletedTask : OpenPathAsync(path);

    public async Task OpenPathAsync(string path)
    {
        var existing = Documents.FirstOrDefault(d => string.Equals(d.FilePath, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            ActiveDocument = existing;
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
        if (ActiveDocument is { } document)
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

    private string ProtectedReason(string path) =>
        _locations.IsMachineInstallation && path.StartsWith(_locations.DataRoot, StringComparison.OrdinalIgnoreCase)
            ? "This label is in the machine's LEdit folder, which is protected (enable AllowWritingMachineLabels in settings to change that)."
            : "This label is a template packaged with the editor and cannot be replaced.";

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

        await SaveSettingsAsync(_settings with { SnapToGrid = Context.SnapToGrid, ShowGrid = Context.ShowGrid });
        return true;
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

    // ---------------------------------------------------------------- Edit

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo() => Session?.Undo();

    private bool CanUndo() => Session is { History.CanUndo: true, IsInteracting: false };

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo() => Session?.Redo();

    private bool CanRedo() => Session is { History.CanRedo: true, IsInteracting: false };

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Cut() => Session?.CutSelection(Context.Clipboard);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Copy() => Session?.CopySelection(Context.Clipboard);

    [RelayCommand(CanExecute = nameof(CanPaste))]
    private void Paste() => Session?.Paste(Context.Clipboard, PasteStep);

    private bool CanPaste() => Session is not null && Context.Clipboard.HasContent;

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Delete() => Session?.DeleteSelection();

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Duplicate() => Session?.DuplicateSelection(PasteStep);

    [RelayCommand]
    private void SelectAll() => Session?.SelectAll();

    private bool HasSelection() => Session is { Selection.IsEmpty: false, IsInteracting: false };

    private int PasteStep => Session?.Layout.Header.GridSize is > 0 and var g ? g : 1000;

    // ---------------------------------------------------------------- Arrange

    /// <summary>Arrange commands by name: LockSize, UnlockSize, AlignLeft, AlignRight, AlignTop, AlignBottom, CenterH, CenterV, SameWidth, SameHeight, SameSize, SpaceAcross, SpaceDown, SnapToGrid, Front, Back.</summary>
    [RelayCommand(CanExecute = nameof(CanArrange))]
    private void Arrange(string? operation)
    {
        if (Session is not { } s)
        {
            return;
        }

        _ = operation switch
        {
            "AlignLeft" => s.Align(Alignment.Left),
            "AlignRight" => s.Align(Alignment.Right),
            "AlignTop" => s.Align(Alignment.Top),
            "AlignBottom" => s.Align(Alignment.Bottom),
            "CenterH" => s.Align(Alignment.CenterHorizontallyInLabel),
            "CenterV" => s.Align(Alignment.CenterVerticallyInLabel),
            "SameWidth" => s.MatchSize(SizeMatch.Width),
            "SameHeight" => s.MatchSize(SizeMatch.Height),
            "SameSize" => s.MatchSize(SizeMatch.Both),
            "SpaceAcross" => s.Distribute(Distribution.Horizontal),
            "SpaceDown" => s.Distribute(Distribution.Vertical),
            "SnapToGrid" => s.SnapSelectionToGrid(),
            "Front" => s.BringToFront(),
            "Back" => s.SendToBack(),
            "LockSize" => s.SetSizeLocked(true),
            "UnlockSize" => s.SetSizeLocked(false),
            _ => false,
        };
    }

    private bool CanArrange(string? operation) => HasSelection();

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

    private async Task RememberRecentAsync(string path)
    {
        var settings = _settings.WithRecentFile(path);
        RecentFiles.Clear();
        foreach (var file in settings.RecentFiles)
        {
            RecentFiles.Add(file);
        }

        await SaveSettingsAsync(settings);
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
