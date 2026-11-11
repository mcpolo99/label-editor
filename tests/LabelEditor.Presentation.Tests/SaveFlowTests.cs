using LabelEditor.Application;
using LabelEditor.Application.Abstractions;
using LabelEditor.Application.Documents;
using LabelEditor.Application.Editing;
using LabelEditor.Application.Localization;
using LabelEditor.Application.Preview;
using LabelEditor.Application.Settings;
using LabelEditor.Domain.Fields;
using LabelEditor.Domain.Geometry;
using LabelEditor.Domain.Layout;
using LabelEditor.Presentation.Services;
using LabelEditor.Presentation.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;

namespace LabelEditor.Presentation.Tests;

public class SaveFlowTests
{
    private static readonly string Work = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "le-work"));
    private static readonly string Templates = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "le-templates"));

    private sealed class Store : ILabelDocumentStore
    {
        public Dictionary<string, LabelLayout> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

        public int Saves { get; private set; }

        public Task<LabelLayout> LoadAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(Files[path]);

        public Task SaveAsync(LabelLayout layout, string path, CancellationToken cancellationToken = default)
        {
            Saves++;
            Files[path] = layout;
            return Task.CompletedTask;
        }
    }

    private sealed class Locations : IStorageLocations
    {
        public string DataRoot => Templates;

        public bool IsMachineInstallation => false;

        public IReadOnlyList<LabelFolder> LabelFolders => [];

        public string WorkingFolder => Work;

        public bool IsWriteProtected(string path) => path.StartsWith(Templates, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class Dialogs : IUserInteraction
    {
        public Queue<ReplaceAnswer> ReplaceAnswers { get; } = new();

        public List<string?> ReplaceQuestions { get; } = [];

        public string? SaveAsPath { get; set; }

        public Task<ReplaceAnswer> AskReplaceExistingAsync(string path, string? protectedReason)
        {
            ReplaceQuestions.Add(protectedReason);
            return Task.FromResult(ReplaceAnswers.Dequeue());
        }

        public Task<string?> PickLabelSaveLocationAsync(string suggestedName, string? initialFolder) => Task.FromResult(SaveAsPath);

        public Task<string?> PickLabelToOpenAsync(string? initialFolder) => Task.FromResult<string?>(null);

        public Task<string?> PickBitmapAsync(string? initialFolder) => Task.FromResult<string?>(null);

        public Task<string?> PickPreviewDataAsync(string? initialFolder) => Task.FromResult<string?>(null);


        public Task<string?> PickFolderAsync(string title, string? initialFolder) => Task.FromResult<string?>(null);

        public Task<SaveChangesAnswer> AskSaveChangesAsync(string documentTitle) => Task.FromResult(SaveChangesAnswer.Cancel);

        /// <summary>When false, any error dialog fails the test.</summary>
        public bool ErrorsExpected { get; set; }

        public List<string> Errors { get; } = [];

        public Task ShowErrorAsync(string title, string message)
        {
            if (!ErrorsExpected)
            {
                throw new InvalidOperationException(message);
            }

            Errors.Add(message);
            return Task.CompletedTask;
        }

        public Task ShowMessageAsync(string title, string message) => Task.CompletedTask;

        public Queue<bool> Confirmations { get; } = new();

        public List<string> Questions { get; } = [];

        public Task<bool> ConfirmAsync(string title, string message, string confirmButton)
        {
            Questions.Add(message);
            return Task.FromResult(Confirmations.Dequeue());
        }

        public Task<IReadOnlyList<int>> PickFieldsAsync(FieldTextResolver resolver) => Task.FromResult<IReadOnlyList<int>>([]);

        public Task<int?> PickFieldAsync(FieldTextResolver resolver, int? currentFieldId) => Task.FromResult<int?>(null);

        public HistoryChoice? HistoryChoice { get; set; }

        public Task<HistoryChoice?> ShowHistoryAsync(string labelTitle, IReadOnlyList<LabelVersionInfo> versions) => Task.FromResult(HistoryChoice);
    }

    private sealed class Nothing : IFieldCatalogSource, ILanguageSource, ISettingsStore, ILabelLibrary, IFontCatalog, ILabelVersionHistory
    {
        public Task<IReadOnlyList<LabelVersionInfo>> GetVersionsAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<LabelVersionInfo>>([]);

        public Task<LabelLayout> LoadVersionAsync(string path, int number, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IReadOnlyList<string> FontFamilies => [];

        public Task<FieldCatalog> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(FieldCatalog.Empty);

        public Task<IReadOnlyList<LanguageInfo>> GetLanguagesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<LanguageInfo>>([]);

        public Task<TextCatalog> LoadAsync(string languageName, CancellationToken cancellationToken = default) => Task.FromResult(TextCatalog.Empty);

        Task<EditorSettings> ISettingsStore.LoadAsync(CancellationToken cancellationToken) => Task.FromResult(new EditorSettings());

        public Task SaveAsync(EditorSettings settings, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<LabelLibraryItem>> GetItemsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<LabelLibraryItem>>([]);
    }

    /// <summary>Reports the entries it is given as overflowing.</summary>
    private sealed class Overflows : ITextOverflowCheck
    {
        public List<TextOverflow> Found { get; } = [];

        public int Checks { get; private set; }

        public IReadOnlyList<TextOverflow> Find(LabelLayout layout, FieldTextResolver resolver, IReadOnlyList<PreviewRecord> records)
        {
            Checks++;
            return Found;
        }
    }

    private sealed class History : ILabelVersionHistory
    {
        public LabelLayout Version1 { get; } = LabelLayout.CreateNew(50_000, 30_000);

        public Task<IReadOnlyList<LabelVersionInfo>> GetVersionsAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LabelVersionInfo>>([new LabelVersionInfo(1, DateTimeOffset.UnixEpoch, "someone", null, 100)]);

        public Task<LabelLayout> LoadVersionAsync(string path, int number, CancellationToken cancellationToken = default) => Task.FromResult(Version1);
    }

    private static (MainViewModel Vm, Store Store, Dialogs Dialogs) Setup(ILabelVersionHistory? history = null) => Setup(history, new Overflows());

    private static (MainViewModel Vm, Store Store, Dialogs Dialogs) Setup(ILabelVersionHistory? history, Overflows overflows)
    {
        var store = new Store();
        var dialogs = new Dialogs();
        var nothing = new Nothing();
        var locations = new Locations();
        var vm = new MainViewModel(
            new DocumentService(store, locations),
            new ReferenceDataService(nothing, nothing),
            nothing,
            locations,
            nothing,
            new EditorContext(new EntryClipboard(), nothing),
            dialogs,
            Array.Empty<IPreviewDataSource>(),
            history ?? nothing,
            new ApplicationInfo("1.0.0", null, "Development build", "", "", Work, ""),
            overflows,
            NullLogger<MainViewModel>.Instance);
        return (vm, store, dialogs);
    }

    private static async Task<DocumentViewModel> OpenEditedAsync(MainViewModel vm, Store store, string path)
    {
        store.Files[path] = LabelLayout.CreateNew();
        await vm.OpenPathAsync(path);
        var document = vm.ActiveDocument!;
        document.Session.AddEntry(EntryFactory.Create(EditorTool.Ellipse, new LabelRect(0, 0, 5_000, 5_000)));
        return document;
    }

    [Fact]
    public async Task Opened_labels_are_editable_and_the_first_save_asks_to_replace()
    {
        var (vm, store, dialogs) = Setup();
        var path = Path.Combine(Work, "Mine.xml");
        var document = await OpenEditedAsync(vm, store, path);
        dialogs.ReplaceAnswers.Enqueue(ReplaceAnswer.Replace);

        Assert.True(await vm.SaveDocumentAsync(document));
        document.Session.AddEntry(EntryFactory.Create(EditorTool.Line, new LabelRect(0, 0, 5_000, 0)));
        Assert.True(await vm.SaveDocumentAsync(document));

        Assert.Single(dialogs.ReplaceQuestions); // asked once, not again
        Assert.Null(dialogs.ReplaceQuestions[0]);
        Assert.Equal(2, store.Saves);
        Assert.False(document.Session.IsDirty);
    }

    [Fact]
    public async Task Cancelling_the_replace_question_writes_nothing()
    {
        var (vm, store, dialogs) = Setup();
        var document = await OpenEditedAsync(vm, store, Path.Combine(Work, "Mine.xml"));
        dialogs.ReplaceAnswers.Enqueue(ReplaceAnswer.Cancel);

        Assert.False(await vm.SaveDocumentAsync(document));
        Assert.Equal(0, store.Saves);
        Assert.True(document.Session.IsDirty);
    }

    [Fact]
    public async Task Protected_labels_can_be_edited_but_are_saved_under_a_new_name()
    {
        var (vm, store, dialogs) = Setup();
        var template = Path.Combine(Templates, "LayoutDef.xml");
        var document = await OpenEditedAsync(vm, store, template);
        var original = store.Files[template];
        dialogs.ReplaceAnswers.Enqueue(ReplaceAnswer.SaveAs);
        dialogs.SaveAsPath = Path.Combine(Work, "MyLayout.xml");

        Assert.True(await vm.SaveDocumentAsync(document));

        Assert.NotNull(dialogs.ReplaceQuestions[0]); // the dialog explained why replacing is not offered
        Assert.Same(original, store.Files[template]);
        Assert.Equal(dialogs.SaveAsPath, document.FilePath);
    }

    [Fact]
    public async Task Restoring_a_version_is_an_undoable_edit_of_the_open_label()
    {
        var history = new History();
        var (vm, store, dialogs) = Setup(history);
        var document = await OpenEditedAsync(vm, store, Path.Combine(Work, "Mine.xml"));
        var before = document.Session.Layout;
        dialogs.HistoryChoice = new HistoryChoice(1, HistoryAction.Restore);

        await vm.ShowHistoryCommand.ExecuteAsync(null);

        Assert.Same(history.Version1, document.Session.Layout);
        Assert.True(document.Session.IsDirty);
        document.Session.Undo();
        Assert.Same(before, document.Session.Layout);
    }

    [Fact]
    public async Task Opening_a_version_as_copy_adds_an_untitled_tab()
    {
        var history = new History();
        var (vm, store, dialogs) = Setup(history);
        await OpenEditedAsync(vm, store, Path.Combine(Work, "Mine.xml"));
        dialogs.HistoryChoice = new HistoryChoice(1, HistoryAction.OpenAsCopy);

        await vm.ShowHistoryCommand.ExecuteAsync(null);

        Assert.Equal(2, vm.Documents.Count);
        Assert.Null(vm.ActiveDocument!.FilePath);
        Assert.Same(history.Version1, vm.ActiveDocument.Session.Layout);
    }

    [Fact]
    public async Task Opened_and_saved_labels_are_listed_newest_first_under_open_recent()
    {
        var (vm, store, dialogs) = Setup();
        var first = Path.Combine(Work, "First.xml");
        var second = Path.Combine(Work, "Second.xml");
        store.Files[first] = LabelLayout.CreateNew();
        store.Files[second] = LabelLayout.CreateNew();

        await vm.OpenPathAsync(first);
        await vm.OpenPathAsync(second);
        await vm.OpenPathAsync(first);

        Assert.Equal([first, second], vm.RecentFiles.Select(r => r.Path));
        Assert.Equal(["_1  First.xml", "_2  Second.xml"], vm.RecentFiles.Select(r => r.Header));
        Assert.True(vm.HasRecentFiles);

        await vm.ClearRecentFilesCommand.ExecuteAsync(null);
        Assert.Empty(vm.RecentFiles);
        Assert.False(vm.ClearRecentFilesCommand.CanExecute(null));
    }

    [Fact]
    public async Task A_recent_file_that_no_longer_exists_is_explained_and_removed_from_the_list()
    {
        var (vm, _, dialogs) = Setup();
        var gone = Path.Combine(Work, Guid.NewGuid().ToString("N") + ".xml");
        await vm.InitializeAsync(new EditorSettings { RecentFiles = [gone] }, []);
        dialogs.ErrorsExpected = true;

        await vm.OpenRecentCommand.ExecuteAsync(gone);

        Assert.Contains("no longer exists", Assert.Single(dialogs.Errors), StringComparison.Ordinal);
        Assert.Empty(vm.RecentFiles);
        Assert.DoesNotContain(vm.Documents, d => d.FilePath is not null);
    }

    [Fact]
    public void Recent_file_headers_keep_underscores_in_file_names_literal()
    {
        Assert.Equal("_3  my__label.xml", new RecentFileItem(@"C:\labels\my_label.xml", 3).Header);
        Assert.Equal("10  b.xml", new RecentFileItem(@"C:\labels\b.xml", 10).Header);
    }

    [Fact]
    public async Task Saving_warns_about_texts_that_do_not_fit_and_cancelling_selects_them()
    {
        var overflows = new Overflows();
        var (vm, store, dialogs) = Setup(null, overflows);
        var document = await OpenEditedAsync(vm, store, Path.Combine(Work, "Mine.xml"));
        var tooLong = document.Layout.Entries[^1].Id;
        overflows.Found.Add(new TextOverflow(tooLong, "A very long customer name", "P7"));
        vm.WarnTextOverflowOnSave = true;
        dialogs.Confirmations.Enqueue(false);

        Assert.False(await vm.SaveDocumentAsync(document));

        Assert.Equal(0, store.Saves);
        Assert.Contains("\"A very long customer name\"  (record P7)", Assert.Single(dialogs.Questions), StringComparison.Ordinal);
        Assert.Equal([tooLong], document.Session.Selection.Ids);
        Assert.Empty(dialogs.ReplaceQuestions); // stopped before anything else was asked
    }

    [Fact]
    public async Task Saving_anyway_writes_the_label()
    {
        var overflows = new Overflows();
        var (vm, store, dialogs) = Setup(null, overflows);
        var document = await OpenEditedAsync(vm, store, Path.Combine(Work, "Mine.xml"));
        overflows.Found.Add(new TextOverflow(document.Layout.Entries[^1].Id, "x", null));
        vm.WarnTextOverflowOnSave = true;
        dialogs.Confirmations.Enqueue(true);
        dialogs.ReplaceAnswers.Enqueue(ReplaceAnswer.Replace);

        Assert.True(await vm.SaveDocumentAsync(document));
        Assert.Equal(1, store.Saves);
    }

    [Fact]
    public async Task The_fit_check_is_off_unless_turned_on()
    {
        var overflows = new Overflows();
        var (vm, store, dialogs) = Setup(null, overflows);
        var document = await OpenEditedAsync(vm, store, Path.Combine(Work, "Mine.xml"));
        overflows.Found.Add(new TextOverflow(document.Layout.Entries[^1].Id, "x", null));
        dialogs.ReplaceAnswers.Enqueue(ReplaceAnswer.Replace);

        Assert.True(await vm.SaveDocumentAsync(document));
        Assert.Empty(dialogs.Questions);
        Assert.Equal(0, overflows.Checks);
    }
}
