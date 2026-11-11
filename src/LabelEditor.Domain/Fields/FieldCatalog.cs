using LabelEditor.Domain.Common;

namespace LabelEditor.Domain.Fields;

/// <summary>
/// A reference into the LEdit language tables, e.g. "(95, 9010, L)" = section 95, number 9010,
/// with "L" as the text to use when the table has no entry. Plain captions have no section.
/// </summary>
public sealed record TextReference(int? Section, int? Number, string Fallback)
{
    public static TextReference Literal(string text) => new(null, null, text);

    public bool IsLocalizable => Section is not null && Number is not null;

    public override string ToString() => IsLocalizable ? $"({Section}, {Number}, {Fallback})" : Fallback;
}

/// <summary>An NC data field that a text element can print.</summary>
/// <param name="Id">LEdit field id written to <c>ElementID</c>.</param>
/// <param name="Key">
/// Element name from the field tree (e.g. <c>QMin</c>, <c>FinalLength</c>). This is the field's
/// primary display name; the translated caption is secondary information only.
/// </param>
/// <param name="Caption">Translatable caption, e.g. "(95, 9014, Quantità)".</param>
public sealed record DataFieldDefinition(int Id, string Key, TextReference Caption)
{
    /// <summary>
    /// Unique, language-independent name. Equal to <see cref="Key"/> unless several ids share the same
    /// element name (ListaOriginaliId.xml reuses <c>CustomTexts</c>), in which case the caption fallback
    /// is appended: <c>CustomTexts (MatCode)</c>.
    /// </summary>
    public string Name { get; init; } = Key;
}

/// <summary>A node in the field picker tree.</summary>
public sealed record FieldCategory(string Key, TextReference Caption)
{
    public ValueList<FieldCategory> Children { get; init; } = [];

    public ValueList<DataFieldDefinition> Fields { get; init; } = [];

    public IEnumerable<DataFieldDefinition> AllFields() => Fields.Concat(Children.SelectMany(c => c.AllFields()));
}

/// <summary>All data fields known to the editor, organised as the LEdit field tree.</summary>
public sealed class FieldCatalog
{
    /// <summary>The LEdit id of "Raw text" (fixed text) in the field tree.</summary>
    public const int RawTextId = 40000;

    private readonly Dictionary<int, DataFieldDefinition> _byId;

    public FieldCatalog(FieldCategory root)
    {
        _byId = [];
        foreach (var field in root.AllFields())
        {
            _byId.TryAdd(field.Id, field);
        }

        // Disambiguate element names shared by different ids, then rebuild the tree with those names.
        var duplicated = _byId.Values.GroupBy(f => f.Key, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var field in _byId.Values.Where(f => duplicated.Contains(f.Key)).ToList())
        {
            _byId[field.Id] = field with { Name = $"{field.Key} ({field.Caption.Fallback})" };
        }

        Root = duplicated.Count == 0 ? root : Rename(root);
    }

    private FieldCategory Rename(FieldCategory category) => category with
    {
        Children = category.Children.Select(Rename).ToValueList(),
        Fields = category.Fields.Select(f => _byId.GetValueOrDefault(f.Id) is { } named && named.Key == f.Key ? f with { Name = named.Name } : f).ToValueList(),
    };

    public static FieldCatalog Empty { get; } = new(new FieldCategory("root", TextReference.Literal("Fields")));

    public FieldCategory Root { get; }

    public IReadOnlyCollection<DataFieldDefinition> Fields => _byId.Values;

    public DataFieldDefinition? Find(int id) => _byId.GetValueOrDefault(id);
}
