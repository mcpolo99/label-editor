using LabelEditor.Application.Localization;
using LabelEditor.Domain.Entries;
using LabelEditor.Domain.Fields;
using LabelEditor.Domain.Text;

namespace LabelEditor.Application.Preview;

/// <summary>
/// Turns text entries into the strings shown while designing. Data fields are identified by their
/// element name from the field tree (e.g. <c>[QMin]</c>), never by a translated caption, so a label
/// reads the same whatever language the editor runs in.
/// </summary>
public sealed class FieldTextResolver(FieldCatalog catalog, TextCatalog texts)
{
    public FieldCatalog Catalog { get; } = catalog;

    public TextCatalog Texts { get; } = texts;

    /// <summary>Field name, e.g. "QMin"; "#45999" for ids missing from the catalog.</summary>
    public string FieldName(int fieldId) => Catalog.Find(fieldId)?.Name ?? $"#{fieldId}";

    /// <summary>Translated caption, for tooltips only.</summary>
    public string? FieldCaption(int fieldId) => Catalog.Find(fieldId) is { } f ? Texts.Resolve(f.Caption) : null;

    /// <summary>Printed text using each element's preview value, or the "[FieldName]" placeholder when it has none.</summary>
    public string PreviewText(TextEntry entry) =>
        TextComposer.Compose(entry, (field, element) => string.IsNullOrEmpty(element.PreviewText) ? $"[{FieldName(field.FieldId)}]" : element.PreviewText);

    /// <summary>Structural description, e.g. <c>"L: " + [FinalLength]</c>.</summary>
    public string Describe(TextEntry entry) =>
        entry.Elements.Count == 0
            ? "(empty)"
            : string.Join(" + ", entry.Elements.Select(e => e.Source switch
            {
                LiteralText literal => $"\"{literal.Text}\"",
                DataFieldReference field => $"[{FieldName(field.FieldId)}]",
                _ => "?",
            }));

    public string DescribeElement(TextElement element) => element.Source switch
    {
        LiteralText literal => $"\"{literal.Text}\"",
        DataFieldReference field => $"[{FieldName(field.FieldId)}]",
        _ => "?",
    };
}
