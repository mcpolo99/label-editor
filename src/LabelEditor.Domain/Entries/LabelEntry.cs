using LabelEditor.Domain.Geometry;

namespace LabelEditor.Domain.Entries;

/// <summary>Entry kinds. Values are the LEdit <c>type</c> attribute codes.</summary>
public enum EntryKind
{
    Unknown = 0,
    Text = 2,
    Bitmap = 3,
    LabelGraphic = 4,
    Line = 5,
    Ellipse = 6,
    CornerLogo = 8,
    DataRecord = 9,
}

/// <summary>Stable in-memory identity of an entry. Not persisted: LEdit files have no entry ids.</summary>
public readonly record struct EntryId(Guid Value)
{
    public static EntryId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("N")[..8];
}

/// <summary>
/// Base of every element placed on a label. Entries are immutable; edits produce new instances
/// via <c>with</c> expressions, which keeps undo/redo a matter of keeping old layouts around.
/// </summary>
public abstract record LabelEntry
{
    public required EntryId Id { get; init; }

    /// <summary>The LEdit "Field" rectangle.</summary>
    public required LabelRect Bounds { get; init; }

    /// <summary>
    /// Editor-only: the entry can be moved but not resized (handles, size boxes and "make same size" leave it alone).
    /// Not part of the LEdit format; persisted beside the label by the document store.
    /// </summary>
    public bool IsSizeLocked { get; init; }

    public abstract EntryKind Kind { get; }

    /// <summary>False for entries that exist in the file but are not drawn on the label (the data record).</summary>
    public virtual bool IsVisual => true;

    /// <summary>Whether this entry is a segment (bounds are signed start→end) rather than a box.</summary>
    public virtual bool IsSegment => false;

    /// <summary>A copy with a fresh identity (for duplicate/paste).</summary>
    public LabelEntry CloneWithNewId() => this with { Id = EntryId.New() };
}
