using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LabelEditor.Application.Documents;
using LabelEditor.Application.Editing;
using LabelEditor.Domain.Editing;
using LabelEditor.Domain.Entries;
using LabelEditor.Domain.Fields;
using LabelEditor.Domain.Geometry;
using LabelEditor.Domain.Text;
using LabelEditor.Domain.Layout;
using LabelEditor.Presentation.Services;
using LabelEditor.Presentation.ViewModels.Properties;

namespace LabelEditor.Presentation.ViewModels;

/// <summary>An entry in the outline list.</summary>
public sealed record EntryListItem(EntryId Id, string Kind, string Description, string Position);

/// <summary>One open label document (a tab).</summary>
public sealed partial class DocumentViewModel : ObservableObject, IDisposable
{
    private readonly IUserInteraction _interaction;
    private bool _syncingList;

    public DocumentViewModel(EditorSession session, EditorContext context, IUserInteraction interaction)
    {
        Session = session;
        Context = context;
        _interaction = interaction;
        Session.Changed += OnSessionChanged;
        Context.PropertyChanged += OnContextChanged;
        Context.Clipboard.Changed += OnClipboardChanged;
        RebuildList();
        RefreshProperties();
    }

    public EditorSession Session { get; }

    public EditorContext Context { get; }

    public LabelLayout Layout => Session.Layout;

    public string Title => Session.Title + (Session.IsDirty ? " *" : string.Empty);

    public string? FilePath => Session.Location?.Path;

    public bool IsReadOnly => Session.Location?.IsReadOnly ?? false;

    public string ToolTip => FilePath is null ? "Not saved yet" : FilePath + (IsReadOnly ? "\nProtected location: saving asks for a new name." : string.Empty);

    public ObservableCollection<EntryListItem> Entries { get; } = [];

    /// <summary>Zoom factor, 1 = physical size.</summary>
    [ObservableProperty]
    private double _zoom = 2.0;

    [ObservableProperty]
    private EntryListItem? _selectedListItem;

    [ObservableProperty]
    private PropertiesViewModel? _properties;

    /// <summary>Pointer position on the label, for the status bar.</summary>
    [ObservableProperty]
    private string _cursorPosition = string.Empty;

    /// <summary>
    /// Inserts a data field. With a position (drag and drop): appended to the text entry under it, or a new
    /// text entry there. Without: appended to the selected text entry, or a new entry below the others.
    /// </summary>
    public void InsertField(int fieldId, LabelPoint? position)
    {
        if (position is null && Properties is TextPropertiesViewModel text)
        {
            text.AddField(fieldId);
            return;
        }

        if (position is { } p && HitTesting.EntryAt(Layout, p, 0) is TextEntry target)
        {
            Session.Execute("Add field", l => l.ReplaceEntry(target with { Elements = target.Elements.Add(TextElement.ForField(fieldId)) }), [target.Id]);
            return;
        }

        var at = position ?? NextFreePosition();
        if (Context.SnapToGrid)
        {
            at = LayoutUnits.Snap(at, Layout.Header.GridSize);
        }

        Session.AddEntry(fieldId == FieldCatalog.RawTextId
            ? EntryFactory.Create(EditorTool.Text, new LabelRect(at.X, at.Y, 30_000, 5_000))
            : EntryFactory.CreateFieldText(fieldId, at));
    }

    private LabelPoint NextFreePosition()
    {
        var bottom = Layout.VisualEntries.Select(e => e.Bounds.Bottom).DefaultIfEmpty(0).Max();
        var y = bottom + 5_000 > Layout.Header.Shape.Height ? 2_000 : bottom + 1_000;
        return new LabelPoint(2_000, y);
    }

    // ---------------------------------------------------------------- editing commands
    // The main menu, the toolbars and the canvas context menu all use these, so behaviour and enabled state match.

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo() => Session.Undo();

    private bool CanUndo() => Session is { History.CanUndo: true, IsInteracting: false };

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo() => Session.Redo();

    private bool CanRedo() => Session is { History.CanRedo: true, IsInteracting: false };

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Cut() => Session.CutSelection(Context.Clipboard);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Copy() => Session.CopySelection(Context.Clipboard);

    [RelayCommand(CanExecute = nameof(CanPaste))]
    private void Paste() => Session.Paste(Context.Clipboard, PasteStep);

    private bool CanPaste() => Context.Clipboard.HasContent && !Session.IsInteracting;

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Delete() => Session.DeleteSelection();

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Duplicate() => Session.DuplicateSelection(PasteStep);

    [RelayCommand]
    private void SelectAll() => Session.SelectAll();

    private bool HasSelection() => !Session.Selection.IsEmpty && !Session.IsInteracting;

    private int PasteStep => Layout.Header.GridSize > 0 ? Layout.Header.GridSize : 1000;

    /// <summary>
    /// Arrange commands by name: AlignLeft, AlignRight, AlignTop, AlignBottom, CenterH, CenterV, SameWidth, SameHeight,
    /// SameSize, SpaceAcross, SpaceDown, SnapToGrid, Front, Back, LockSize, UnlockSize, ToggleLock.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanArrange))]
    private void Arrange(string? operation)
    {
        var s = Session;
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
            "ToggleLock" => s.SetSizeLocked(!s.SelectedEntries().All(e => e.IsSizeLocked)),
            _ => false,
        };
    }

    /// <summary>Aligning and sizing need a reference plus at least one other entry; spacing needs three.</summary>
    public bool CanArrange(string? operation)
    {
        if (Session.IsInteracting)
        {
            return false;
        }

        var count = Session.SelectedEntries().Count(e => e.IsVisual);
        return operation switch
        {
            "AlignLeft" or "AlignRight" or "AlignTop" or "AlignBottom" or "SameWidth" or "SameHeight" or "SameSize" => count >= 2,
            "SpaceAcross" or "SpaceDown" => count >= 3,
            _ => count >= 1,
        };
    }

    /// <summary>True when every selected entry is size-locked (drives the lock toggle button).</summary>
    public bool IsSelectionSizeLocked => Session.SelectedEntries() is { Count: > 0 } selected && selected.All(e => e.IsSizeLocked);

    /// <summary>Right-click on the canvas: select the entry under the pointer unless it is already part of the selection.</summary>
    public void SelectForContextMenu(EntryId? hit)
    {
        if (hit is { } id && !Session.Selection.Contains(id))
        {
            Session.Selection.Select(id);
        }
    }

    private void OnClipboardChanged(object? sender, EventArgs e) => PasteCommand.NotifyCanExecuteChanged();

    private void NotifyCommands()
    {
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
        CutCommand.NotifyCanExecuteChanged();
        CopyCommand.NotifyCanExecuteChanged();
        PasteCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        DuplicateCommand.NotifyCanExecuteChanged();
        ArrangeCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(IsSelectionSizeLocked));
    }

    /// <summary>Raised when the canvas must repaint (layout, selection or context changed).</summary>
    public event EventHandler? RenderInvalidated;

    public void Dispose()
    {
        Session.Changed -= OnSessionChanged;
        Context.PropertyChanged -= OnContextChanged;
        Context.Clipboard.Changed -= OnClipboardChanged;
    }

    partial void OnZoomChanged(double value) => RenderInvalidated?.Invoke(this, EventArgs.Empty);

    partial void OnSelectedListItemChanged(EntryListItem? value)
    {
        if (!_syncingList && value is not null && !Session.IsInteracting)
        {
            Session.Selection.Select(value.Id);
        }
    }

    private void OnSessionChanged(object? sender, SessionChangedEventArgs e)
    {
        NotifyCommands();

        if (e.Change.HasFlag(SessionChange.Layout) && !e.Change.HasFlag(SessionChange.Preview))
        {
            RebuildList();
        }

        if (e.Change.HasFlag(SessionChange.Layout) || e.Change.HasFlag(SessionChange.Selection))
        {
            SyncListSelection();
            RefreshProperties();
            RenderInvalidated?.Invoke(this, EventArgs.Empty);
        }

        if (e.Change.HasFlag(SessionChange.Document))
        {
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(FilePath));
            OnPropertyChanged(nameof(IsReadOnly));
            OnPropertyChanged(nameof(ToolTip));
            OnPropertyChanged(nameof(Layout));
        }
    }

    private void OnContextChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EditorContext.Resolver) or nameof(EditorContext.PreviewRecord))
        {
            RebuildList();
            Properties = null;
            RefreshProperties();
        }

        RenderInvalidated?.Invoke(this, EventArgs.Empty);
    }

    private void RebuildList()
    {
        _syncingList = true;
        try
        {
            Entries.Clear();
            var resolver = Context.Resolver;
            foreach (var entry in Layout.VisualEntries)
            {
                var description = entry switch
                {
                    TextEntry t => resolver.Describe(t),
                    BitmapEntry b => Path.GetFileName(b.Path),
                    LabelGraphicEntry g => $"Draw {g.GraphicIndex + 1}",
                    UnknownEntry u => $"<{u.ElementName}>",
                    _ => string.Empty,
                };
                var kind = (entry is TextEntry { IsBarcode: true } ? "Bar code" : entry.Kind.ToString()) + (entry.IsSizeLocked ? " 🔒" : string.Empty);
                var b2 = entry.Bounds;
                Entries.Add(new EntryListItem(entry.Id, kind, description, $"{b2.X / 1000.0:0.##}, {b2.Y / 1000.0:0.##}"));
            }
        }
        finally
        {
            _syncingList = false;
        }

        SyncListSelection();
    }

    private void SyncListSelection()
    {
        _syncingList = true;
        try
        {
            var dominant = Session.Selection.Dominant;
            SelectedListItem = dominant is { } id ? Entries.FirstOrDefault(i => i.Id == id) : null;
        }
        finally
        {
            _syncingList = false;
        }
    }

    /// <summary>Keeps the same panel instance while the same entry stays selected, so focus is not lost while typing.</summary>
    private void RefreshProperties()
    {
        var selection = Session.Selection;
        if (selection.Count > 1)
        {
            Properties = new MultiSelectionViewModel(Session, selection.Count);
            return;
        }

        if (selection.Dominant is not { } id || Layout.Find(id) is not { } entry)
        {
            if (Properties is LabelSettingsViewModel settings)
            {
                settings.Refresh(Layout);
            }
            else
            {
                Properties = new LabelSettingsViewModel(Session);
            }

            return;
        }

        if (Properties is EntryPropertiesViewModel current && current.Id == id)
        {
            current.Refresh(entry);
            return;
        }

        Properties = entry switch
        {
            TextEntry t => new TextPropertiesViewModel(Session, t, Context, _interaction),
            EllipseEntry el => new EllipsePropertiesViewModel(Session, el),
            LineEntry l => new LinePropertiesViewModel(Session, l),
            BitmapEntry b => new BitmapPropertiesViewModel(Session, b, _interaction),
            LabelGraphicEntry g => new LabelGraphicPropertiesViewModel(Session, g),
            CornerLogoEntry c => new CornerLogoPropertiesViewModel(Session, c),
            UnknownEntry u => new UnknownEntryPropertiesViewModel(Session, u),
            _ => new LabelSettingsViewModel(Session),
        };
    }
}
