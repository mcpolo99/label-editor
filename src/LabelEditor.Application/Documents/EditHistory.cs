using LabelEditor.Domain.Layout;

namespace LabelEditor.Application.Documents;

/// <summary>One undoable step: the layout before and after a named edit.</summary>
/// <param name="Description">Shown in the Undo menu.</param>
/// <param name="Before">Layout before the edit.</param>
/// <param name="After">Layout after the edit.</param>
/// <param name="MergeKey">Consecutive steps with the same key (e.g. typing in one property box) merge into one.</param>
public sealed record HistoryStep(string Description, LabelLayout Before, LabelLayout After, string? MergeKey = null);

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

    /// <summary>Records a step, merging it into the previous one when both carry the same merge key.</summary>
    public void Push(HistoryStep step)
    {
        if (step.MergeKey is not null && _redo.Count == 0 && _undo.Last is { } last && last.Value.MergeKey == step.MergeKey)
        {
            last.Value = last.Value with { After = step.After };
            return;
        }

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
