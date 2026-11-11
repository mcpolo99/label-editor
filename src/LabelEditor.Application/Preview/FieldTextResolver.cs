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
    /// Value of a field element: from <paramref name="record"/> when given (by element name, then unique
    /// name, then the caption's fallback text — which is what tells the shared <c>CustomTexts</c> ids apart,
    /// e.g. MatCode), else the element's own preview text, else the "[FieldName]" placeholder.
    /// </summary>
    public string FieldValue(int fieldId, TextElement element, PreviewRecord? record) =>
        TryFieldValue(fieldId, element, record) ?? $"[{FieldName(fieldId)}]";

    /// <summary>The field's value from the record or preview text; null when only a placeholder is available.</summary>
    public string? TryFieldValue(int fieldId, TextElement element, PreviewRecord? record)
    {
        if (record is not null && Catalog.Find(fieldId) is { } field &&
            (record.Find(field.Key) ?? record.Find(field.Name) ?? record.Find(field.Caption.Fallback)) is { } value)
        {
            return value;
        }

        return string.IsNullOrEmpty(element.PreviewText) ? null : element.PreviewText;
    }

    /// <summary>Printed text of an entry, using <paramref name="record"/> values when given.</summary>
    public string PreviewText(TextEntry entry, PreviewRecord? record = null) =>
        TextComposer.Compose(entry, (field, element) => FieldValue(field.FieldId, element, record));

    /// <summary>
    /// Data to encode in a bar code. Fields without a value get <paramref name="sample"/> instead of the
    /// "[Name]" placeholder, so the symbol still renders realistically while designing.
    /// </summary>
    public string BarcodeData(TextEntry entry, PreviewRecord? record, string sample = "123456789") =>
        TextComposer.Compose(entry, (field, element) => TryFieldValue(field.FieldId, element, record) ?? sample);

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
