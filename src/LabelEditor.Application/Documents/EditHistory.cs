using LabelEditor.Domain.Layout;

namespace LabelEditor.Application.Documents;

/// <summary>One undoable step: the layout before and after a named edit.</summary>
public sealed record HistoryStep(string Description, LabelLayout Before, LabelLayout After);

/// <summary>
/// Undo/redo stack of layout snapshots. Layouts are immutable and share unchanged entries,
/// so a snapshot costs little more than the entries that actually changed.
/// </summary>
public sealed class EditHistory
{
    private readonly LinkedList<HistoryStep> _undo = new();
    private readonly Stack<HistoryStep> _redo = new();

    public EditHistory(int capacity = 200) => Capacity = Math.Max(1, capacity);

    public int Capacity { get; }

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public string? UndoDescription => _undo.Last?.Value.Description;

    public string? RedoDescription => _redo.TryPeek(out var step) ? step.Description : null;

    public void Push(HistoryStep step)
    {
        _undo.AddLast(step);
        if (_undo.Count > Capacity)
        {
            _undo.RemoveFirst();
        }

        _redo.Clear();
    }

    public HistoryStep? Undo()
    {
        if (_undo.Last is not { } node)
        {
            return null;
        }

        _undo.RemoveLast();
        _redo.Push(node.Value);
        return node.Value;
    }

    public HistoryStep? Redo()
    {
        if (!_redo.TryPop(out var step))
        {
            return null;
        }

        _undo.AddLast(step);
        return step;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }
}
