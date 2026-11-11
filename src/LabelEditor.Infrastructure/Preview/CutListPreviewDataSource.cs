using System.Text;
using System.Xml.Linq;
using LabelEditor.Application.Abstractions;
using LabelEditor.Application.Preview;
using LabelEditor.Infrastructure.Ledit;

namespace LabelEditor.Infrastructure.Preview;

/// <summary>
/// Reads generated label sheets (<c>*_lbl.xml</c>): a <c>&lt;CutList&gt;</c> with one <c>&lt;Part&gt;</c>
/// per label. Part attributes are named after field-tree elements (IDesc, CabCode, Description11, …),
/// so they map onto fields directly; a few short names are aliased to their fields.
/// </summary>
public sealed class CutListPreviewDataSource : IPreviewDataSource
{
    /// <summary>CutList attribute → additional field names it fills.</summary>
    private static readonly Dictionary<string, string[]> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["L"] = ["FinalLength", "Length"],
        ["W"] = ["FinalWidth", "Width"],
        ["Material"] = ["MatCode"],
        ["id"] = ["PartId"],
    };

    public bool CanRead(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            using var stream = LeditFieldCatalogSource.OpenRead(path);
            var buffer = new byte[512];
            var read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
            return Encoding.UTF8.GetString(buffer, 0, read).Contains("<CutList", StringComparison.Ordinal);
        }
        catch (IOException)
        {
            return false;
        }
    }

    public async Task<PreviewDataSet> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        XDocument document;
        try
        {
            await using var stream = LeditFieldCatalogSource.OpenRead(path);
            document = await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is System.Xml.XmlException or IOException or UnauthorizedAccessException)
        {
            throw new LabelDocumentException($"Could not read preview data '{path}': {ex.Message}", ex);
        }

        if (document.Root?.Name.LocalName != "CutList")
        {
            throw new LabelDocumentException($"'{path}' is not a label sheet (expected <CutList>).");
        }

        var board = document.Root.Element("Board");
        var records = document.Root.Elements("Part").Select(part => ToRecord(part, board)).ToList();
        return new PreviewDataSet(path, $"{Path.GetFileName(path)} – {records.Count} parts", records);
    }

    private static PreviewRecord ToRecord(XElement part, XElement? board)
    {
        var values = new List<KeyValuePair<string, string>>();
        foreach (var attribute in part.Attributes())
        {
            var name = attribute.Name.LocalName;
            values.Add(new(name, attribute.Value));
            if (Aliases.TryGetValue(name, out var aliases))
            {
                values.AddRange(aliases.Select(a => new KeyValuePair<string, string>(a, attribute.Value)));
            }
        }

        if (board?.Attribute("Thickness")?.Value is { } thickness)
        {
            values.Add(new("Thickness", thickness));
        }

        var name2 = part.Attribute("IDesc")?.Value ?? part.Attribute("id")?.Value ?? "Part";
        return new PreviewRecord(name2, values);
    }
}
