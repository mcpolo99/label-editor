using CommunityToolkit.Mvvm.ComponentModel;
using LabelEditor.Application;
using LabelEditor.Application.Editing;
using LabelEditor.Application.Preview;
using LabelEditor.Presentation.Services;

namespace LabelEditor.Presentation.ViewModels;

/// <summary>State shared by all open documents: reference data, clipboard, editor options.</summary>
public sealed partial class EditorContext : ObservableObject
{
    public EditorContext(EntryClipboard clipboard, IFontCatalog fonts)
    {
        Clipboard = clipboard;
        Fonts = fonts;
        _resolver = new ReferenceData(Domain.Fields.FieldCatalog.Empty, Application.Localization.TextCatalog.Empty, []).CreateResolver();
    }

    public EntryClipboard Clipboard { get; }

    public IFontCatalog Fonts { get; }

    /// <summary>Field names and captions for the current language. Replaced when the language changes.</summary>
    [ObservableProperty]
    private FieldTextResolver _resolver;

    [ObservableProperty]
    private bool _snapToGrid = true;

    [ObservableProperty]
    private bool _showGrid = true;

    [ObservableProperty]
    private EditorTool _activeTool = EditorTool.Select;

    /// <summary>Data record the canvas fills fields with; null = design view (preview texts / field names).</summary>
    [ObservableProperty]
    private PreviewRecord? _previewRecord;
}
