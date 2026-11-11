using LabelEditor.Application.Abstractions;
using LabelEditor.Application.Documents;
using LabelEditor.Application.Editing;
using LabelEditor.Application.Localization;
using LabelEditor.Application.Preview;
using LabelEditor.Domain.Entries;
using LabelEditor.Domain.Fields;
using LabelEditor.Domain.Geometry;
using LabelEditor.Domain.Layout;
using LabelEditor.Domain.Text;

namespace LabelEditor.Application.Tests;

public class DocumentServiceTests
{
    private sealed class MemoryStore : ILabelDocumentStore
    {
        public Dictionary<string, LabelLayout> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

        public TaskCompletionSource? SaveGate { get; set; }

        public Task<LabelLayout> LoadAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(Files[path]);

        public async Task SaveAsync(LabelLayout layout, string path, CancellationToken cancellationToken = default)
        {
            if (SaveGate is { } gate)
            {
                await gate.Task;
            }

            Files[path] = layout;
        }
    }

    private sealed class Locations(string protectedFolder) : IStorageLocations
    {
        public string DataRoot => protectedFolder;

        public bool IsMachineInstallation => true;

        public IReadOnlyList<LabelFolder> LabelFolders => [];

        public string WorkingFolder => Path.GetTempPath();

        public bool IsWriteProtected(string path) => path.StartsWith(protectedFolder, StringComparison.OrdinalIgnoreCase);
    }

    private static readonly string Protected = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "protected"));
    private static readonly string Writable = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "writable", "a.xml"));

    [Fact]
    public async Task Files_from_protected_folders_open_read_only()
    {
        var store = new MemoryStore();
        var path = Path.Combine(Protected, "x.xml");
        store.Files[path] = LabelLayout.CreateNew();
        var service = new DocumentService(store, new Locations(Protected));

        var session = await service.OpenAsync(path);

        Assert.True(session.Location!.IsReadOnly);
        Assert.False(DocumentService.CanSaveInPlace(session));
        await Assert.ThrowsAsync<LabelDocumentException>(() => service.SaveAsAsync(session, path));
    }

    [Fact]
    public async Task Edits_made_while_saving_keep_the_document_dirty()
    {
        var store = new MemoryStore { SaveGate = new TaskCompletionSource() };
        var service = new DocumentService(store, new Locations(Protected));
        var session = DocumentService.CreateNew();

        var save = service.SaveAsAsync(session, Writable);
        session.AddEntry(EntryFactory.Create(EditorTool.Ellipse, new LabelRect(0, 0, 1_000, 1_000)));
        store.SaveGate.SetResult();
        await save;

        Assert.True(session.IsDirty);
        Assert.Equal(Writable, session.Location?.Path);
    }

    [Fact]
    public async Task Open_as_copy_has_no_location()
    {
        var store = new MemoryStore();
        var path = Path.Combine(Protected, "template.xml");
        store.Files[path] = LabelLayout.CreateNew();

        var session = await new DocumentService(store, new Locations(Protected)).OpenAsCopyAsync(path);

        Assert.Null(session.Location);
        Assert.False(session.IsDirty);
    }

    [Fact]
    public void Preview_uses_element_names_for_fields_without_sample_values()
    {
        var catalog = new FieldCatalog(new FieldCategory("root", TextReference.Literal("r"))
        {
            Fields = [new DataFieldDefinition(45808, "QMin", new TextReference(95, 9014, "Quantità"))],
        });
        var texts = new TextCatalog("English", new Dictionary<(int, int), LocalizedText> { [(95, 9014)] = new("Quantity", null) });
        var resolver = new FieldTextResolver(catalog, texts);
        var entry = new TextEntry
        {
            Id = EntryId.New(),
            Bounds = default,
            Elements = [TextElement.ForLiteral("Q: "), TextElement.ForField(45808), TextElement.ForField(99999)],
        };

        Assert.Equal("Q: [QMin][#99999]", resolver.PreviewText(entry));
        Assert.Equal("\"Q: \" + [QMin] + [#99999]", resolver.Describe(entry));
        Assert.Equal("Quantity", resolver.FieldCaption(45808));
    }
}
