using System.Globalization;
using LabelEditor.Application;
using LabelEditor.Domain.Entries;
using LabelEditor.Domain.Fields;
using LabelEditor.Domain.Text;
using LabelEditor.Infrastructure.Ledit;
using Microsoft.Extensions.Logging.Abstractions;

namespace LabelEditor.Infrastructure.Tests;

public class LeditReferenceDataTests
{
    private static Task<FieldCatalog> LoadCatalogAsync() =>
        new LeditFieldCatalogSource(TestData.DefaultsOnly(), NullLogger<LeditFieldCatalogSource>.Instance).LoadAsync();

    [Fact]
    public async Task Fields_are_named_by_their_element_name_not_their_translation()
    {
        var catalog = await LoadCatalogAsync();

        var qmin = catalog.Find(45808);
        Assert.NotNull(qmin);
        Assert.Equal("QMin", qmin.Name);
        Assert.Equal(new TextReference(95, 9014, "Quantità"), qmin.Caption);
        Assert.Equal("FinalLength", catalog.Find(45804)?.Name);
    }

    [Fact]
    public async Task Field_names_shared_by_several_ids_are_disambiguated()
    {
        var catalog = await LoadCatalogAsync();

        Assert.Equal("CustomTexts (MatCode)", catalog.Find(45867)?.Name);
        Assert.Equal("CustomTexts (QPiece)", catalog.Find(45866)?.Name);
        Assert.Equal(catalog.Fields.Count, catalog.Fields.Select(f => f.Name).Distinct().Count());
    }

    [Fact]
    public async Task Field_tree_keeps_the_ledit_categories()
    {
        var catalog = await LoadCatalogAsync();

        var keys = catalog.Root.Children.Select(c => c.Key).ToList();
        Assert.Contains("WorlistData", keys);
        Assert.Contains("PartData", keys);
        Assert.Contains("PartDescriptions", keys);
        Assert.Contains("AllFields", keys);
        Assert.NotNull(catalog.Find(FieldCatalog.RawTextId));
        Assert.Equal("All fields", catalog.Root.Children.Single(c => c.Key == "AllFields").Caption.Fallback);
    }

    [Fact]
    public async Task The_edge_fields_of_every_side_are_grouped_for_the_edge_indicator()
    {
        var catalog = await LoadCatalogAsync();

        var edges = catalog.Root.Children.Single(c => c.Key == "Edges");
        Assert.Equal(new TextReference(0, 10, "Edges"), edges.Caption); // LEdit's translated "Edges"
        Assert.Equal(
            ["EdgingInfo", "TypeEdgeUp", "MatEdgeUp", "ColEdgeUp", "TypeEdgeLo", "MatEdgeLo", "ColEdgeLo", "TypeEdgeL", "MatEdgeL", "ColEdgeL", "TypeEdgeR", "MatEdgeR", "ColEdgeR"],
            edges.Fields.Select(f => f.Key));
        Assert.Equal(45862, catalog.Fields.Single(f => f.Name == "MatEdgeR").Id);
    }

    [Fact]
    public async Task Almost_all_field_ids_used_by_real_labels_are_in_the_catalog()
    {
        var catalog = await LoadCatalogAsync();
        var format = new LeditLabelFormat();
        var used = new HashSet<int>();
        foreach (var file in TestData.AllLabels())
        {
            await using var stream = File.OpenRead(file);
            var layout = await format.ReadAsync(stream);
            used.UnionWith(layout.Entries.OfType<TextEntry>().SelectMany(t => t.Elements).Select(e => e.Source).OfType<DataFieldReference>().Select(f => f.FieldId));
        }

        var missing = used.Where(id => catalog.Find(id) is null).Order().ToList();

        // Unknown ids still load and save (shown as "#id"); this guards against catalog parsing regressions.
        Assert.True(missing.Count <= used.Count / 10, $"Missing field ids: {string.Join(", ", missing)}");
    }

    [Theory]
    [InlineData("English", "Quantity")]
    [InlineData("Svenska", "Mängd")]
    [InlineData("Ruski", "Количество")]
    [InlineData("Chinese", "数量")]
    [InlineData("Polski", "Ilość")]
    public async Task Language_tables_decode_with_the_right_code_page(string language, string expected)
    {
        var source = new LeditLanguageSource(TestData.DefaultsOnly(), NullLogger<LeditLanguageSource>.Instance);

        var texts = await source.LoadAsync(language);

        Assert.Equal(expected, texts.Find(95, 9014)?.Caption);
    }

    [Fact]
    public void Language_lines_split_caption_and_description()
    {
        Assert.True(LeditLanguageSource.TryParseLine(@"95 9008 Length\\Cut dimension A", out var key, out var text));

        Assert.Equal((95, 9008), key);
        Assert.Equal("Length", text.Caption);
        Assert.Equal("Cut dimension A", text.Description);
    }

    [Fact]
    public async Task Picks_the_language_mapped_to_the_ui_culture()
    {
        var source = new LeditLanguageSource(TestData.DefaultsOnly(), NullLogger<LeditLanguageSource>.Instance);
        var languages = await source.GetLanguagesAsync();

        Assert.Equal("Svenska", ReferenceDataService.ChooseLanguage(languages, null, CultureInfo.GetCultureInfo("sv-SE")));
        Assert.Equal("Deutsch", ReferenceDataService.ChooseLanguage(languages, null, CultureInfo.GetCultureInfo("de-AT")));
        Assert.Equal("English", ReferenceDataService.ChooseLanguage(languages, null, CultureInfo.GetCultureInfo("ja-JP")));
        Assert.Equal("Italiano", ReferenceDataService.ChooseLanguage(languages, "italiano", CultureInfo.GetCultureInfo("sv-SE")));
    }

    [Theory]
    [InlineData("(95, 9014, Quantità)", 95, 9014, "Quantità")]
    [InlineData("(95, 9055, Disegno Sagoma)", 95, 9055, "Disegno Sagoma")]
    [InlineData("1 - 10", null, null, "1 - 10")]
    public void Parses_text_references(string text, int? section, int? number, string fallback) =>
        Assert.Equal(new TextReference(section, number, fallback), LeditFieldCatalogSource.ParseTextReference(text));
}
