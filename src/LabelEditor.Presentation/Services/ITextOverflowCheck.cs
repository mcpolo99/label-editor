using LabelEditor.Application.Preview;
using LabelEditor.Domain.Entries;
using LabelEditor.Domain.Layout;

namespace LabelEditor.Presentation.Services;

/// <summary>A text that does not fit its field when printed at its font size.</summary>
/// <param name="EntryId">The text entry.</param>
/// <param name="Text">The text as it prints.</param>
/// <param name="RecordKey">The preview record it was filled from; null = the design sample values.</param>
public sealed record TextOverflow(EntryId EntryId, string Text, string? RecordKey);

/// <summary>Measures texts with the real fonts (implemented by the UI layer, which has them).</summary>
public interface ITextOverflowCheck
{
    /// <summary>
    /// Texts with a fixed font size that are wider than their field (or, with word wrap, taller), filled with
    /// the design sample values and with each of <paramref name="records"/>. One result per entry: the first
    /// record it overflows with. Fields without a sample value are only judged with records.
    /// </summary>
    IReadOnlyList<TextOverflow> Find(LabelLayout layout, FieldTextResolver resolver, IReadOnlyList<PreviewRecord> records);
}
