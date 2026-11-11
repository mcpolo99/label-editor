using LabelEditor.Domain.Common;
using LabelEditor.Domain.Text;

namespace LabelEditor.Domain.Entries;

/// <summary>A text field (optionally rendered as a bar code). LEdit type 2.</summary>
public sealed record TextEntry : LabelEntry
{
    public override EntryKind Kind => EntryKind.Text;

    public FontSpec Font { get; init; } = FontSpec.Default;

    public TextOptions Options { get; init; } = new();

    public ValueList<TextElement> Elements { get; init; } = [];

    public bool IsBarcode => Options.Barcode.IsBarcode;
}
