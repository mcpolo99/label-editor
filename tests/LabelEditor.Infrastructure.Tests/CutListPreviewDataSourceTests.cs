using LabelEditor.Application.Preview;
using LabelEditor.Infrastructure.Ledit;
using LabelEditor.Infrastructure.Preview;
using Microsoft.Extensions.Logging.Abstractions;

namespace LabelEditor.Infrastructure.Tests;

public class CutListPreviewDataSourceTests
{
    private static string SamplePath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "PreviewData", "sample_lbl.xml");

    [Fact]
    public async Task Reads_one_record_per_part_keyed_by_field_name()
    {
        var source = new CutListPreviewDataSource();

        Assert.True(source.CanRead(SamplePath));
        Assert.False(source.CanRead(TestData.Label("Plats128C.xml")));
        var data = await source.LoadAsync(SamplePath);

        Assert.Equal(3, data.Records.Count);
        var first = data.Records[0];
        Assert.Equal("P1", first.Name);
        Assert.Equal("Customer 1", first.Find("cabcode"));
        Assert.Equal("260", first.Find("FinalLength"));
        Assert.Equal("16", first.Find("Thickness"));
    }

    [Fact]
    public async Task Fills_a_real_label_with_sheet_values()
    {
        var catalog = await new LeditFieldCatalogSource(TestData.DefaultsOnly(), NullLogger<LeditFieldCatalogSource>.Instance).LoadAsync();
        var resolver = new FieldTextResolver(catalog, Application.Localization.TextCatalog.Empty);
        var data = await new CutListPreviewDataSource().LoadAsync(SamplePath);
        await using var stream = File.OpenRead(TestData.Label("Plats128C_V3.xml"));
        var layout = await new LeditLabelFormat().ReadAsync(stream);

        var texts = layout.Entries.OfType<Domain.Entries.TextEntry>().Select(t => resolver.PreviewText(t, data.Records[0])).ToList();

        Assert.Contains("100000001", texts); // CabInfo (45836) bar code value
        Assert.Contains("260", texts);       // FinalLength
        Assert.DoesNotContain(texts, t => t.Contains("[CabInfo]", StringComparison.Ordinal));
    }
}
