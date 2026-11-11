using CommunityToolkit.Mvvm.ComponentModel;
using LabelEditor.Application.Documents;
using LabelEditor.Domain.Entries;
using LabelEditor.Domain.Geometry;

namespace LabelEditor.Presentation.ViewModels.Properties;

/// <summary>
/// Base of the property panels. Values are pushed into the panel with Refresh whenever the
/// session changes (undo, drag, ...); user edits flow back as session edits. The refresh guard keeps
/// those two directions from feeding each other.
/// </summary>
public abstract class PropertiesViewModel : ObservableObject
{
    private bool _refreshing;

    protected PropertiesViewModel(EditorSession session) => Session = session;

    protected EditorSession Session { get; }

    public abstract string Title { get; }

    protected bool IsRefreshing => _refreshing;

    protected void RunRefresh(Action refresh)
    {
        _refreshing = true;
        try
        {
            refresh();
        }
        finally
        {
            _refreshing = false;
        }
    }

    protected static decimal ToDisplay(int layoutUnits) => (decimal)LayoutUnits.ToDisplay(layoutUnits);

    protected static int FromDisplay(decimal? value) => LayoutUnits.FromDisplay((double)(value ?? 0));
}

/// <summary>Properties shared by all entries: the field rectangle.</summary>
public abstract partial class EntryPropertiesViewModel : PropertiesViewModel
{
    protected EntryPropertiesViewModel(EditorSession session, LabelEntry entry)
        : base(session)
    {
        Id = entry.Id;
        IsSegment = entry.IsSegment;
    }

    public EntryId Id { get; }

    /// <summary>Lines show signed width/height (their direction); boxes show sizes.</summary>
    public bool IsSegment { get; }

    public string SizeLabel => IsSegment ? "Δx / Δy" : "Width / Height";

    [ObservableProperty]
    private decimal? _x;

    [ObservableProperty]
    private decimal? _y;

    [ObservableProperty]
    private decimal? _width;

    [ObservableProperty]
    private decimal? _height;

    /// <summary>Editor-only lock: the entry moves freely but keeps its size.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanResize))]
    private bool _isSizeLocked;

    public bool CanResize => !IsSizeLocked;

    /// <summary>Editor-only lock: the entry's X / Y stay put; it can still be resized from the right and bottom.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanMove))]
    private bool _isPositionLocked;

    public bool CanMove => !IsPositionLocked;

    /// <summary>Pushes the current entry state into the panel.</summary>
    public void Refresh(LabelEntry entry) => RunRefresh(() =>
    {
        X = ToDisplay(entry.Bounds.X);
        Y = ToDisplay(entry.Bounds.Y);
        Width = ToDisplay(entry.Bounds.Width);
        Height = ToDisplay(entry.Bounds.Height);
        IsSizeLocked = entry.IsSizeLocked;
        IsPositionLocked = entry.IsPositionLocked;
        OnRefresh(entry);
    });

    protected abstract void OnRefresh(LabelEntry entry);

    /// <summary>Applies a property edit as an undo step that merges with further edits of the same property.</summary>
    protected void Update<T>(string property, Func<T, T> change)
        where T : LabelEntry
    {
        if (IsRefreshing || Session.IsInteracting)
        {
            return;
        }

        Session.Execute($"Change {property}", l => l.Find(Id) is T entry ? l.ReplaceEntry(change(entry)) : l, mergeKey: $"{Id}:{property}");
    }

    partial void OnXChanged(decimal? value) => Update<LabelEntry>("position", e => e.IsPositionLocked ? e : e with { Bounds = e.Bounds with { X = FromDisplay(value) } });

    partial void OnYChanged(decimal? value) => Update<LabelEntry>("position", e => e.IsPositionLocked ? e : e with { Bounds = e.Bounds with { Y = FromDisplay(value) } });

    partial void OnIsPositionLockedChanged(bool value) => Update<LabelEntry>(value ? "lock position" : "unlock position", e => e with { IsPositionLocked = value });

    partial void OnWidthChanged(decimal? value) => Update<LabelEntry>("size", e => e.IsSizeLocked ? e : e with { Bounds = e.Bounds with { Width = Clamp(FromDisplay(value)) } });

    partial void OnHeightChanged(decimal? value) => Update<LabelEntry>("size", e => e.IsSizeLocked ? e : e with { Bounds = e.Bounds with { Height = Clamp(FromDisplay(value)) } });

    partial void OnIsSizeLockedChanged(bool value) => Update<LabelEntry>(value ? "lock size" : "unlock size", e => e with { IsSizeLocked = value });

    private int Clamp(int size) => IsSegment ? size : Math.Max(0, size);
}

/// <summary>Shown when several entries are selected.</summary>
public sealed class MultiSelectionViewModel(EditorSession session, int count) : PropertiesViewModel(session)
{
    public override string Title => $"{count} entries selected";

    public string Hint => "Use Arrange to align, size or space them; the first selected entry is the reference. "
        + "Dragging a handle resizes them all. Sizes follow each entry's text direction, so rotated text grows along its own reading direction.";
}
