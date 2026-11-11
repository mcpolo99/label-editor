using LabelEditor.Domain.Entries;

namespace LabelEditor.Application.Documents;

/// <summary>How a selection request combines with the current selection.</summary>
public enum SelectionMode
{
    Replace,
    Add,
    Toggle,
}

/// <summary>
/// Ordered selection. The first selected entry is the dominant one that alignment and
/// size-matching commands use as reference (as in LEdit).
/// </summary>
public sealed class SelectionModel
{
    private readonly List<EntryId> _ids = [];

    public event EventHandler? Changed;

    public IReadOnlyList<EntryId> Ids => _ids;

    public IReadOnlySet<EntryId> AsSet() => _ids.ToHashSet();

    public int Count => _ids.Count;

    public bool IsEmpty => _ids.Count == 0;

    public EntryId? Dominant => _ids.Count > 0 ? _ids[0] : null;

    public bool Contains(EntryId id) => _ids.Contains(id);

    public void Select(IEnumerable<EntryId> ids, SelectionMode mode = SelectionMode.Replace)
    {
        var before = _ids.ToList();
        if (mode == SelectionMode.Replace)
        {
            _ids.Clear();
        }

        foreach (var id in ids)
        {
            if (mode == SelectionMode.Toggle && _ids.Remove(id))
            {
                continue;
            }

            if (!_ids.Contains(id))
            {
                _ids.Add(id);
            }
        }

        RaiseIfChanged(before);
    }

    public void Select(EntryId id, SelectionMode mode = SelectionMode.Replace) => Select([id], mode);

    public void Clear() => Select([], SelectionMode.Replace);

    /// <summary>Drops ids that no longer exist (after undo, delete, ...).</summary>
    public void Retain(Func<EntryId, bool> exists)
    {
        var before = _ids.ToList();
        _ids.RemoveAll(id => !exists(id));
        RaiseIfChanged(before);
    }

    private void RaiseIfChanged(List<EntryId> before)
    {
        if (!before.SequenceEqual(_ids))
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
