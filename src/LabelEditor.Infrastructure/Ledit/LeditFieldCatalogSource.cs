using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using LabelEditor.Application.Abstractions;
using LabelEditor.Domain.Common;
using LabelEditor.Domain.Fields;
using Microsoft.Extensions.Logging;

namespace LabelEditor.Infrastructure.Ledit;

/// <summary>
/// Builds the field catalog from LEdit's <c>LEditTreeXML.xml</c> (the categorised picker tree) and
/// <c>ListaOriginaliId.xml</c> (the flat list of all insertable fields).
/// <para>
/// In both files every element with an <c>ID</c> attribute is a field: its element name is the field's
/// name (<c>&lt;QMin Text='(95, 9014, Quantità)' ID='45808'/&gt;</c> → "QMin", id 45808).
/// Elements without an ID are categories.
/// </para>
/// </summary>
public sealed partial class LeditFieldCatalogSource(IStorageLocations locations, ILogger<LeditFieldCatalogSource> logger) : IFieldCatalogSource
{
    public const string TreeFileName = "LEditTreeXML.xml";
    public const string FlatListFileName = "ListaOriginaliId.xml";

    public async Task<FieldCatalog> LoadAsync(CancellationToken cancellationToken = default)
    {
        var tree = await TryLoadAsync(Path.Combine(locations.DataRoot, TreeFileName), cancellationToken).ConfigureAwait(false);
        var flat = await TryLoadAsync(Path.Combine(locations.DataRoot, FlatListFileName), cancellationToken).ConfigureAwait(false);
        return Build(tree, flat);
    }

    /// <summary>Combines the tree's categories with an "All fields" category from the flat list.</summary>
    public static FieldCatalog Build(XDocument? tree, XDocument? flat)
    {
        var categories = new List<FieldCategory>();
        if (tree?.Root is { } treeRoot)
        {
            categories.AddRange(ParseCategory(treeRoot).Children);
            var rootFields = ParseCategory(treeRoot).Fields;
            if (rootFields.Count > 0)
            {
                categories.Insert(0, new FieldCategory("General", TextReference.Literal("General")) { Fields = rootFields });
            }
        }

        if (flat?.Root is { } flatRoot)
        {
            var all = flatRoot.Descendants().Select(ParseField).OfType<DataFieldDefinition>().ToValueList();

            // The part's edge data per side, which the edge indicator (corner logo) prints: Up / Lo are the length
            // edges (Len1 / Len2), L / R the width edges (Wdt1 / Wdt2). LEdit's own tree only lists a few of them.
            var edges = all.Where(f => EdgeFieldPattern().IsMatch(f.Key)).OrderBy(f => f.Id).ToValueList();
            if (edges.Count > 0)
            {
                categories.Add(new FieldCategory("Edges", new TextReference(0, 10, "Edges")) { Fields = edges });
            }

            // No LEdit text means "all fields" (95/9078 is "Enhanced Data"), so this caption is not translated.
            categories.Add(new FieldCategory("AllFields", TextReference.Literal("All fields")) { Fields = all });
        }

        return new FieldCatalog(new FieldCategory("root", TextReference.Literal("Fields")) { Children = categories.ToValueList() });
    }

    /// <summary>Parses LEdit's "(95, 9014, Quantità)" text references; anything else is a literal caption.</summary>
    public static TextReference ParseTextReference(string? text)
    {
        if (text is null)
        {
            return TextReference.Literal(string.Empty);
        }

        var match = TextReferencePattern().Match(text);
        return match.Success
            ? new TextReference(
                int.Parse(match.Groups["section"].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups["number"].Value, CultureInfo.InvariantCulture),
                match.Groups["fallback"].Value.Trim())
            : TextReference.Literal(text.Trim());
    }

    private static FieldCategory ParseCategory(XElement element)
    {
        var children = new List<FieldCategory>();
        var fields = new List<DataFieldDefinition>();
        foreach (var child in element.Elements())
        {
            if (ParseField(child) is { } field)
            {
                fields.Add(field);
            }
            else
            {
                children.Add(ParseCategory(child));
            }
        }

        return new FieldCategory(element.Name.LocalName, ParseTextReference(element.Attribute("Text")?.Value))
        {
            Children = children.ToValueList(),
            Fields = fields.ToValueList(),
        };
    }

    private static DataFieldDefinition? ParseField(XElement element) =>
        int.TryParse(element.Attribute("ID")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
            ? new DataFieldDefinition(id, element.Name.LocalName, ParseTextReference(element.Attribute("Text")?.Value))
            : null;

    private async Task<XDocument?> TryLoadAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            logger.LogWarning("Field file {Path} not found", path);
            return null;
        }

        try
        {
            await using var stream = OpenRead(path);
            return await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is System.Xml.XmlException or IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "Could not read field file {Path}", path);
            return null;
        }
    }

    internal static FileStream OpenRead(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 16 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);

    [GeneratedRegex(@"^((Type|Mat|Col)Edge(Up|Lo|L|R)|EdgingInfo)$", RegexOptions.CultureInvariant)]
    private static partial Regex EdgeFieldPattern();

    [GeneratedRegex(@"^\(\s*(?<section>\d+)\s*,\s*(?<number>\d+)\s*,(?<fallback>.*)\)\s*$", RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex TextReferencePattern();
}
