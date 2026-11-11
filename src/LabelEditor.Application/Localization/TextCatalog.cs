using LabelEditor.Domain.Fields;

namespace LabelEditor.Application.Localization;

/// <summary>An available LEdit language.</summary>
/// <param name="Name">LEdit language name (also the file name), e.g. "Svenska".</param>
/// <param name="CultureCodes">Culture codes mapped to this language by LanguageMap.xml.</param>
public sealed record LanguageInfo(string Name, IReadOnlyList<string> CultureCodes);

/// <summary>One language-table entry. LEdit separates a short caption and a tooltip with "\\".</summary>
public sealed record LocalizedText(string Caption, string? Description);

/// <summary>Language table lookup (section, number) → text.</summary>
public sealed class TextCatalog
{
    private readonly IReadOnlyDictionary<(int Section, int Number), LocalizedText> _texts;

    public TextCatalog(string languageName, IReadOnlyDictionary<(int Section, int Number), LocalizedText> texts)
    {
        LanguageName = languageName;
        _texts = texts;
    }

    public static TextCatalog Empty { get; } = new("None", new Dictionary<(int, int), LocalizedText>());

    public string LanguageName { get; }

    public int Count => _texts.Count;

    public LocalizedText? Find(int section, int number) => _texts.GetValueOrDefault((section, number));

    /// <summary>Caption for a reference, falling back to the text embedded in it.</summary>
    public string Resolve(TextReference reference) =>
        reference is { Section: { } s, Number: { } n } && Find(s, n) is { } text && text.Caption.Length > 0
            ? text.Caption
            : reference.Fallback;

    /// <summary>LEdit UI string (section 95) with a fallback.</summary>
    public string Ui(int number, string fallback) => Find(LeditSections.Editor, number)?.Caption is { Length: > 0 } caption ? caption : fallback;
}

/// <summary>Known language-table sections.</summary>
public static class LeditSections
{
    /// <summary>The LEdit label editor section.</summary>
    public const int Editor = 95;
}
