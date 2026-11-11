using LabelEditor.Domain.Entries;

namespace LabelEditor.Application.Editing;

/// <summary>
/// In-process clipboard shared by all open documents, so entries can be copied between labels.
/// Entries are immutable, so storing them directly is safe.
/// </summary>
public sealed class EntryClipboard
{
    private IReadOnlyList<LabelEntry> _entries = [];

    public event EventHandler? Changed;

    public bool HasContent => _entries.Count > 0;

    /// <summary>How many times the current content has been pasted (each paste is offset further).</summary>
    public int PasteCount { get; private set; }

    public void Set(IEnumerable<LabelEntry> entries)
    {
        _entries = entries.Where(e => e.IsVisual).ToList();
        PasteCount = 0;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Copies of the content with new ids, offset by <paramref name="step"/> per paste.</summary>
    public IReadOnlyList<LabelEntry> TakeForPaste(int step)
    {
        PasteCount++;
        var offset = step * PasteCount;
        return _entries.Select(e => e.CloneWithNewId() with { Bounds = e.Bounds.Offset(offset, offset) }).ToList();
    }
}
