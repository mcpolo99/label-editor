using LabelEditor.Domain.Entries;
using LabelEditor.Domain.Layout;

namespace LabelEditor.Application.Documents;

/// <summary>What changed in a session, so views can refresh only what they need.</summary>
[Flags]
public enum SessionChange
{
    None = 0,
    Layout = 1,
    Selection = 2,
    History = 4,
    Document = 8,

    /// <summary>Layout changed as part of an uncommitted interactive edit (drag in progress).</summary>
    Preview = 16,
}

/// <summary>Event data for <see cref="EditorSession.Changed"/>.</summary>
public sealed class SessionChangedEventArgs(SessionChange change) : EventArgs
{
    public SessionChange Change { get; } = change;
}

/// <summary>Where a session's document is stored.</summary>
/// <param name="Path">Full file path.</param>
/// <param name="IsReadOnly">True when the location must not be overwritten (machine install, templates).</param>
public sealed record DocumentLocation(string Path, bool IsReadOnly)
{
    public string FileName => System.IO.Path.GetFileNameWithoutExtension(Path);
}

/// <summary>
/// One open label: the current layout, its undo history and selection.
/// <para>
/// Every edit goes through <see cref="Execute"/> or <see cref="BeginInteractive"/>, so there is
/// exactly one place where history is recorded and change notifications are raised. The session is
/// not thread-safe; use it from the UI thread. File I/O happens elsewhere, asynchronously.
/// </para>
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "The caller that began an interactive edit owns and disposes it.")]
public sealed class EditorSession
{
    private LabelLayout _savedLayout;
    private InteractiveEdit? _activeEdit;

    public EditorSession(LabelLayout layout, DocumentLocation? location = null, int historyCapacity = 200)
    {
        Layout = layout;
        _savedLayout = layout;
        Location = location;
        History = new EditHistory(historyCapacity);
        Selection = new SelectionModel();
        Selection.Changed += (_, _) => Raise(SessionChange.Selection);
    }

    public event EventHandler<SessionChangedEventArgs>? Changed;

    public LabelLayout Layout { get; private set; }

    public DocumentLocation? Location { get; private set; }

    public EditHistory History { get; }

    public SelectionModel Selection { get; }

    public bool IsDirty => !ReferenceEquals(Layout, _savedLayout) && Layout != _savedLayout;

    public bool IsInteracting => _activeEdit is not null;

    public string Title => Location?.FileName ?? "Untitled";

    /// <summary>Applies an edit as one undoable step. No-op edits are not recorded.</summary>
    /// <param name="description">Shown in the Undo menu.</param>
    /// <param name="edit">Pure transformation of the current layout.</param>
    /// <param name="selectAfter">Optional new selection (e.g. the pasted entries).</param>
    /// <returns>True if the layout changed.</returns>
    public bool Execute(string description, Func<LabelLayout, LabelLayout> edit, IEnumerable<EntryId>? selectAfter = null)
    {
        EnsureNotInteracting();
        var before = Layout;
        var after = edit(before);
        if (after == before)
        {
            return false;
        }

        History.Push(new HistoryStep(description, before, after));
        SetLayout(after, SessionChange.Layout | SessionChange.History);
        if (selectAfter is not null)
        {
            Selection.Select(selectAfter);
        }

        return true;
    }

    /// <summary>
    /// Starts an edit that updates continuously (dragging, resizing) and is recorded as a single
    /// undo step when committed. Dispose without commit to cancel.
    /// </summary>
    public InteractiveEdit BeginInteractive(string description)
    {
        EnsureNotInteracting();
        _activeEdit = new InteractiveEdit(this, description, Layout);
        return _activeEdit;
    }

    public bool Undo()
    {
        EnsureNotInteracting();
        if (History.Undo() is not { } step)
        {
            return false;
        }

        SetLayout(step.Before, SessionChange.Layout | SessionChange.History);
        return true;
    }

    public bool Redo()
    {
        EnsureNotInteracting();
        if (History.Redo() is not { } step)
        {
            return false;
        }

        SetLayout(step.After, SessionChange.Layout | SessionChange.History);
        return true;
    }

    /// <summary>Records that <paramref name="savedLayout"/> (default: the current layout) was saved to <paramref name="location"/>.</summary>
    /// <remarks>If the user edited while an async save ran, pass the snapshot that was written so the document stays dirty.</remarks>
    public void MarkSaved(DocumentLocation location, LabelLayout? savedLayout = null)
    {
        _savedLayout = savedLayout ?? Layout;
        Location = location;
        Raise(SessionChange.Document);
    }

    internal void ApplyPreview(LabelLayout layout) => SetLayout(layout, SessionChange.Layout | SessionChange.Preview);

    internal void EndInteractive(InteractiveEdit edit, bool commit)
    {
        if (!ReferenceEquals(edit, _activeEdit))
        {
            return;
        }

        _activeEdit = null;
        if (commit && Layout != edit.Origin)
        {
            History.Push(new HistoryStep(edit.Description, edit.Origin, Layout));
            SetLayout(Layout, SessionChange.Layout | SessionChange.History);
        }
        else
        {
            SetLayout(edit.Origin, SessionChange.Layout);
        }
    }

    private void SetLayout(LabelLayout layout, SessionChange change)
    {
        Layout = layout;
        Selection.Retain(id => layout.Find(id) is not null);
        Raise(change | SessionChange.Document);
    }

    private void Raise(SessionChange change) => Changed?.Invoke(this, new SessionChangedEventArgs(change));

    private void EnsureNotInteracting()
    {
        if (_activeEdit is not null)
        {
            throw new InvalidOperationException("Finish the interactive edit before starting another change.");
        }
    }
}

/// <summary>An edit in progress. Each <see cref="Update"/> recomputes the layout from the state at the start.</summary>
public sealed class InteractiveEdit : IDisposable
{
    private readonly EditorSession _session;
    private bool _finished;

    internal InteractiveEdit(EditorSession session, string description, LabelLayout origin)
    {
        _session = session;
        Description = description;
        Origin = origin;
    }

    public string Description { get; }

    /// <summary>The layout when the edit began.</summary>
    public LabelLayout Origin { get; }

    /// <summary>Shows <paramref name="fromOrigin"/>(Origin) without recording history.</summary>
    public void Update(Func<LabelLayout, LabelLayout> fromOrigin)
    {
        ObjectDisposedException.ThrowIf(_finished, this);
        _session.ApplyPreview(fromOrigin(Origin));
    }

    public void Commit() => Finish(commit: true);

    public void Cancel() => Finish(commit: false);

    public void Dispose() => Finish(commit: false);

    private void Finish(bool commit)
    {
        if (_finished)
        {
            return;
        }

        _finished = true;
        _session.EndInteractive(this, commit);
    }
}
