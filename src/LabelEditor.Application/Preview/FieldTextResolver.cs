using LabelEditor.Application.Localization;
using LabelEditor.Domain.Entries;
using LabelEditor.Domain.Fields;
using LabelEditor.Domain.Text;

namespace LabelEditor.Application.Preview;

/// <summary>
/// Turns text entries into the strings shown on the canvas. Data fields are identified by their
/// element name from the field tree (e.g. <c>[QMin]</c>), never by a translated caption, so a label
/// reads the same whatever language the editor runs in, and data files bind to fields by that name.
/// </summary>
public sealed class FieldTextResolver(FieldCatalog catalog, TextCatalog texts)
{
    public FieldCatalog Catalog { get; } = catalog;

    public TextCatalog Texts { get; } = texts;

    /// <summary>Field name, e.g. "QMin"; "#45999" for ids missing from the catalog.</summary>
    public string FieldName(int fieldId) => Catalog.Find(fieldId)?.Name ?? $"#{fieldId}";

    /// <summary>Translated caption, for tooltips only.</summary>
    public string? FieldCaption(int fieldId) => Catalog.Find(fieldId) is { } f ? Texts.Resolve(f.Caption) : null;

    /// <summary>
    /// Value of a field element.
    /// <list type="bullet">
    /// <item>With a data <paramref name="record"/> (data preview): the record's value, found by element name,
    /// then unique name, then the caption's fallback text (which tells the shared <c>CustomTexts</c> ids apart,
    /// e.g. MatCode). A field the record does not provide prints <b>nothing</b>, exactly as on a real label.</item>
    /// <item>Without a record (design view): the element's sample text, else the "[FieldName]" placeholder.</item>
    /// </list>
    /// </summary>
    public string FieldValue(int fieldId, TextElement element, PreviewRecord? record) =>
        record is null
            ? (string.IsNullOrEmpty(element.PreviewText) ? $"[{FieldName(fieldId)}]" : element.PreviewText)
            : RecordValue(fieldId, record) ?? string.Empty;

    /// <summary>The record's value for a field, or null when the record does not provide it.</summary>
    public string? RecordValue(int fieldId, PreviewRecord record) =>
        Catalog.Find(fieldId) is { } field
            ? record.Find(field.Key) ?? record.Find(field.Name) ?? record.Find(field.Caption.Fallback)
            : null;

    /// <summary>Printed text of an entry: data values with a record, sample texts/placeholders without.</summary>
    public string PreviewText(TextEntry entry, PreviewRecord? record = null) =>
        TextComposer.Compose(entry, (field, element) => FieldValue(field.FieldId, element, record));

    /// <summary>
    /// Data to encode in a bar code. With a record, missing fields are empty (an empty bar code is not drawn).
    /// In design view, fields without a sample text use <paramref name="sample"/> so the symbol still looks real.
    /// </summary>
    public string BarcodeData(TextEntry entry, PreviewRecord? record, string sample = "123456789") =>
        TextComposer.Compose(entry, (field, element) => record is null
            ? (string.IsNullOrEmpty(element.PreviewText) ? sample : element.PreviewText)
            : RecordValue(field.FieldId, record) ?? string.Empty);

    /// <summary>Structural description, e.g. <c>"L: " + [FinalLength]</c>.</summary>
    public string Describe(TextEntry entry) =>
        entry.Elements.Count == 0 ? "(empty)" : string.Join(" + ", entry.Elements.Select(DescribeElement));

    public string DescribeElement(TextElement element) => element.Source switch
    {
        LiteralText literal => $"\"{literal.Text}\"",
        DataFieldReference field => $"[{FieldName(field.FieldId)}]",
        _ => "?",
    };
}
