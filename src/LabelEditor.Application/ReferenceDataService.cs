using System.Globalization;
using LabelEditor.Application.Abstractions;
using LabelEditor.Application.Localization;
using LabelEditor.Application.Preview;
using LabelEditor.Domain.Fields;

namespace LabelEditor.Application;

/// <summary>Field catalog and language table currently in use.</summary>
public sealed record ReferenceData(FieldCatalog Fields, TextCatalog Texts, IReadOnlyList<LanguageInfo> Languages)
{
    public FieldTextResolver CreateResolver() => new(Fields, Texts);
}

/// <summary>Loads reference data (fields + language) asynchronously at start-up or on language change.</summary>
public sealed class ReferenceDataService(IFieldCatalogSource fields, ILanguageSource languages)
{
    public const string FallbackLanguage = "English";

    /// <param name="languageName">LEdit language name; null picks one from the current UI culture.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task<ReferenceData> LoadAsync(string? languageName, CancellationToken cancellationToken = default)
    {
        var catalogTask = fields.LoadAsync(cancellationToken);
        var languagesTask = languages.GetLanguagesAsync(cancellationToken);
        await Task.WhenAll(catalogTask, languagesTask).ConfigureAwait(false);

        var available = await languagesTask.ConfigureAwait(false);
        var chosen = ChooseLanguage(available, languageName, CultureInfo.CurrentUICulture);
        var texts = chosen is null ? TextCatalog.Empty : await languages.LoadAsync(chosen, cancellationToken).ConfigureAwait(false);
        return new ReferenceData(await catalogTask.ConfigureAwait(false), texts, available);
    }

    /// <summary>Explicit choice, else the language mapped to the UI culture (or its parent), else English.</summary>
    public static string? ChooseLanguage(IReadOnlyList<LanguageInfo> available, string? requested, CultureInfo culture)
    {
        if (requested is not null && available.FirstOrDefault(l => string.Equals(l.Name, requested, StringComparison.OrdinalIgnoreCase)) is { } explicitChoice)
        {
            return explicitChoice.Name;
        }

        for (var c = culture; !Equals(c, CultureInfo.InvariantCulture); c = c.Parent)
        {
            var match = available.FirstOrDefault(l => l.CultureCodes.Contains(c.Name, StringComparer.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match.Name;
            }
        }

        return available.FirstOrDefault(l => l.Name == FallbackLanguage)?.Name ?? (available.Count > 0 ? available[0].Name : null);
    }
}
