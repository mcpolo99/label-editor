using System.Globalization;
using System.Text;
using System.Xml.Linq;
using LabelEditor.Application.Abstractions;
using LabelEditor.Application.Localization;
using Microsoft.Extensions.Logging;

namespace LabelEditor.Infrastructure.Ledit;

/// <summary>
/// Reads LEdit language tables: <c>Language/&lt;Name&gt;.txt</c>, one entry per line as
/// <c>SS NNNN Caption\\Description</c>, encoded in the language's Windows code page; plus
/// <c>LanguageMap.xml</c>, which maps OS culture codes to LEdit language names.
/// </summary>
public sealed class LeditLanguageSource(IStorageLocations locations, ILogger<LeditLanguageSource> logger) : ILanguageSource
{
    public const string LanguageFolder = "Language";
    public const string LanguageMapFileName = "LanguageMap.xml";

    // Verified against the shipped files: decoding with these code pages yields correct text.
    private static readonly Dictionary<string, int> CodePages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Ruski"] = 1251,
        ["Chinese"] = 936,
        ["Hebrew"] = 1255,
        ["Polski"] = 1250,
        ["Cesko"] = 1250,
        ["Slowenski"] = 1250,
        ["Româna"] = 1250,
    };

    static LeditLanguageSource() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static Encoding EncodingFor(string languageName) =>
        Encoding.GetEncoding(CodePages.GetValueOrDefault(languageName, 1252));

    public async Task<IReadOnlyList<LanguageInfo>> GetLanguagesAsync(CancellationToken cancellationToken = default)
    {
        if (locations.DataRoot is not { } root)
        {
            return []; // no LEdit installation: no languages
        }

        var folder = Path.Combine(root, LanguageFolder);
        if (!Directory.Exists(folder))
        {
            logger.LogWarning("Language folder {Folder} not found", folder);
            return [];
        }

        var cultures = await ReadLanguageMapAsync(cancellationToken).ConfigureAwait(false);
        return Directory.EnumerateFiles(folder, "*.txt")
            .Select(Path.GetFileNameWithoutExtension)
            .OfType<string>()
            .Order(StringComparer.CurrentCulture)
            .Select(name => new LanguageInfo(name, cultures.TryGetValue(name, out var codes) ? codes : []))
            .ToList();
    }

    public async Task<TextCatalog> LoadAsync(string languageName, CancellationToken cancellationToken = default)
    {
        if (locations.DataRoot is not { } root)
        {
            return TextCatalog.Empty;
        }

        var path = Path.Combine(root, LanguageFolder, languageName + ".txt");
        if (!File.Exists(path))
        {
            logger.LogWarning("Language file {Path} not found", path);
            return TextCatalog.Empty;
        }

        await using var stream = LeditFieldCatalogSource.OpenRead(path);
        using var reader = new StreamReader(stream, EncodingFor(languageName), detectEncodingFromByteOrderMarks: false);
        var texts = new Dictionary<(int, int), LocalizedText>();
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (TryParseLine(line, out var key, out var text))
            {
                texts.TryAdd(key, text);
            }
        }

        return new TextCatalog(languageName, texts);
    }

    /// <summary>Parses "95 9014 Quantity\\Number of parts" → ((95, 9014), "Quantity", "Number of parts").</summary>
    public static bool TryParseLine(string line, out (int Section, int Number) key, out LocalizedText text)
    {
        key = default;
        text = new LocalizedText(string.Empty, null);
        var span = line.AsSpan();
        var firstSpace = span.IndexOf(' ');
        if (firstSpace <= 0)
        {
            return false;
        }

        var rest = span[(firstSpace + 1)..];
        var secondSpace = rest.IndexOf(' ');
        var numberPart = secondSpace < 0 ? rest : rest[..secondSpace];
        if (!int.TryParse(span[..firstSpace], NumberStyles.None, CultureInfo.InvariantCulture, out var section) ||
            !int.TryParse(numberPart, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
        {
            return false;
        }

        var value = secondSpace < 0 ? string.Empty : rest[(secondSpace + 1)..].ToString();
        var separator = value.IndexOf("\\\\", StringComparison.Ordinal);
        var caption = separator < 0 ? value : value[..separator];
        var description = separator < 0 ? null : value[(separator + 2)..];
        key = (section, number);
        text = new LocalizedText(Unescape(caption).TrimEnd(), description is null ? null : Unescape(description).Trim());
        return true;
    }

    private static string Unescape(string value) => value.Replace("\\n", "\n", StringComparison.Ordinal);

    private async Task<Dictionary<string, IReadOnlyList<string>>> ReadLanguageMapAsync(CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        if (locations.DataRoot is not { } root || Path.Combine(root, LanguageMapFileName) is var path && !File.Exists(path))
        {
            return result;
        }

        await using var stream = LeditFieldCatalogSource.OpenRead(path);
        var document = await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken).ConfigureAwait(false);
        foreach (var group in document.Descendants("Language")
                     .Select(e => (Code: e.Attribute("LanguageCode")?.Value, Name: e.Attribute("LEditLanguageCode")?.Value))
                     .Where(x => x.Code is not null && x.Name is not null)
                     .GroupBy(x => x.Name!, StringComparer.OrdinalIgnoreCase))
        {
            result[group.Key] = group.Select(x => x.Code!).ToList();
        }

        return result;
    }
}
