using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using LabelEditor.App.Rendering;
using LabelEditor.Application.Localization;
using LabelEditor.Application.Preview;
using LabelEditor.Infrastructure.Ledit;
using LabelEditor.Infrastructure.Preview;
using LabelEditor.Infrastructure.Storage;
using LabelEditor.Rendering.Scene;
using Microsoft.Extensions.Logging.Abstractions;

[assembly: AvaloniaTestApplication(typeof(LabelEditor.App.Tests.TestAppBuilder))]

namespace LabelEditor.App.Tests;

public static class TestAppBuilder
{
    // Real Skia rendering (not headless drawing) so text and shapes are actually rasterised.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

public class RenderingTests
{
    private static string Fixture(params string[] parts) => Path.Combine([AppContext.BaseDirectory, "Fixtures", .. parts]);

    /// <summary>PNGs land in the test output folder for visual review.</summary>
    private static string OutputFile(string name) => Path.Combine(AppContext.BaseDirectory, "rendered", name);

    private static async Task<(LabelSceneBuilder Builder, Domain.Layout.LabelLayout Layout)> LoadAsync(string label)
    {
        var locations = new StorageLocations(new StorageOptions
        {
            PackagedDefaultsRoot = Path.Combine(AppContext.BaseDirectory, "Defaults", "LEdit"),
            MachineInstallationRoot = null,
            WorkingFolder = Path.GetTempPath(),
        });
        var catalog = await new LeditFieldCatalogSource(locations, NullLogger<LeditFieldCatalogSource>.Instance).LoadAsync();
        await using var stream = File.OpenRead(Fixture("Labels", label));
        var layout = await new LeditLabelFormat().ReadAsync(stream);
        return (new LabelSceneBuilder(new FieldTextResolver(catalog, TextCatalog.Empty)), layout);
    }

    private static int Render(LabelScene scene, string file, double zoom = 3)
    {
        var scale = ViewTransform.ScaleFor(zoom, scene.Unit);
        var size = new PixelSize((int)Math.Ceiling(scene.Width * scale), (int)Math.Ceiling(scene.Height * scale));
        using var bitmap = new RenderTargetBitmap(size, new Vector(96, 96));
        using (var context = bitmap.CreateDrawingContext())
        {
            context.FillRectangle(Brushes.White, new Rect(0, 0, size.Width, size.Height));
            new ScenePainter().Paint(context, scene, new ViewTransform(scale, default));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        bitmap.Save(file);
        return CountDarkPixels(bitmap);
    }

    private static int CountDarkPixels(Bitmap bitmap)
    {
        using var copy = new WriteableBitmap(bitmap.PixelSize, bitmap.Dpi, Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Premul);
        using (var buffer = copy.Lock())
        {
            bitmap.CopyPixels(buffer, Avalonia.Platform.AlphaFormat.Premul);
            var bytes = new byte[buffer.RowBytes * buffer.Size.Height];
            System.Runtime.InteropServices.Marshal.Copy(buffer.Address, bytes, 0, bytes.Length);
            var dark = 0;
            for (var i = 0; i < bytes.Length; i += 4)
            {
                if (bytes[i] < 100 && bytes[i + 1] < 100 && bytes[i + 2] < 100 && bytes[i + 3] > 200)
                {
                    dark++;
                }
            }

            return dark;
        }
    }

    [AvaloniaFact]
    public async Task Renders_a_real_label_in_design_view()
    {
        var (builder, layout) = await LoadAsync("Plats128C_V3.xml");

        var ink = Render(builder.Build(layout), OutputFile("Plats128C_V3-design.png"));

        Assert.True(ink > 2_000, $"Expected text, bar code and shapes to be drawn; got {ink} dark pixels.");
    }

    [AvaloniaFact]
    public async Task Renders_the_edge_indicator_with_banding_only_where_there_is_an_edge()
    {
        var (builder, layout) = await LoadAsync("sample-edges.xml");
        var edges = layout.Entries.OfType<Domain.Entries.CornerLogoEntry>().Single();
        var single = Domain.Layout.LabelLayout.CreateNew().AddEntry(edges); // as LEdit's preview: just this entry

        var withLeft = Render(builder.Build(single.ReplaceEntry(edges with { TextLeft = "x" })), OutputFile("sample-edges-all-sides.png"));
        var ink = Render(builder.Build(single), OutputFile("sample-edges.png"));

        Assert.True(ink > 300, $"Expected the part outline, bands and texts; got {ink} dark pixels.");
        Assert.True(withLeft > ink + 50, "a left edge should add a band and a text");
    }

    [AvaloniaFact]
    public async Task Renders_a_label_filled_from_a_label_sheet()
    {
        var (builder, layout) = await LoadAsync("Plats128C_V3.xml");
        var data = await new CutListPreviewDataSource().LoadAsync(Fixture("PreviewData", "sample_lbl.xml"));

        var ink = Render(builder.Build(layout, data.Records[0]), OutputFile("Plats128C_V3-P1.png"));

        Assert.True(ink > 2_000);
    }

    [AvaloniaFact]
    public async Task Renders_the_stock_layouts_with_every_entry_kind()
    {
        foreach (var name in new[] { "LayoutDef.xml", "LayoutNest.xml", "LayoutRes.xml", "Lbl_bNest.xml" })
        {
            var (builder, layout) = await LoadAsync(name);

            var ink = Render(builder.Build(layout), OutputFile(Path.ChangeExtension(name, ".png")));

            Assert.True(ink > 500, $"{name}: {ink} dark pixels");
        }
    }
}
