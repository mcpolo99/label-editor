using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LabelEditor.Application.Documents;
using LabelEditor.Domain.Entries;
using LabelEditor.Presentation.Services;

namespace LabelEditor.Presentation.ViewModels.Properties;

/// <summary>Line: geometry only.</summary>
public sealed class LinePropertiesViewModel : EntryPropertiesViewModel
{
    public LinePropertiesViewModel(EditorSession session, LineEntry entry)
        : base(session, entry) => Refresh(entry);

    public override string Title => "Line";

    protected override void OnRefresh(LabelEntry entry)
    {
    }
}

/// <summary>Ellipse pen and fill.</summary>
public sealed partial class EllipsePropertiesViewModel : EntryPropertiesViewModel
{
    public EllipsePropertiesViewModel(EditorSession session, EllipseEntry entry)
        : base(session, entry) => Refresh(entry);

    public override string Title => "Ellipse";

    [ObservableProperty]
    private decimal? _penWidth;

    [ObservableProperty]
    private string _penColor = string.Empty;

    [ObservableProperty]
    private string _brushColor = string.Empty;

    protected override void OnRefresh(LabelEntry entry)
    {
        var e = (EllipseEntry)entry;
        PenWidth = e.PenWidth;
        PenColor = e.PenColor.ToString();
        BrushColor = e.BrushColor.ToString();
    }

    partial void OnPenWidthChanged(decimal? value) => Update<EllipseEntry>("pen width", e => e with { PenWidth = Math.Max(0, (int)(value ?? 1)) });

    partial void OnPenColorChanged(string value)
    {
        if (TryParseColor(value, out var color))
        {
            Update<EllipseEntry>("pen colour", e => e with { PenColor = color });
        }
    }

    partial void OnBrushColorChanged(string value)
    {
        if (TryParseColor(value, out var color))
        {
            Update<EllipseEntry>("fill colour", e => e with { BrushColor = color });
        }
    }

    /// <summary>Accepts "r,g,b" (LEdit's notation).</summary>
    public static bool TryParseColor(string? text, out RgbColor color)
    {
        color = default;
        var parts = text?.Split(',', StringSplitOptions.TrimEntries);
        if (parts is not { Length: 3 } || !byte.TryParse(parts[0], out var r) || !byte.TryParse(parts[1], out var g) || !byte.TryParse(parts[2], out var b))
        {
            return false;
        }

        color = new RgbColor(r, g, b);
        return true;
    }
}

/// <summary>Fixed bitmap file.</summary>
public sealed partial class BitmapPropertiesViewModel : EntryPropertiesViewModel
{
    private readonly IUserInteraction _interaction;

    public BitmapPropertiesViewModel(EditorSession session, BitmapEntry entry, IUserInteraction interaction)
        : base(session, entry)
    {
        _interaction = interaction;
        Refresh(entry);
    }

    public override string Title => "Bitmap";

    [ObservableProperty]
    private string _path = string.Empty;

    [ObservableProperty]
    private bool _fileExists;

    protected override void OnRefresh(LabelEntry entry)
    {
        Path = ((BitmapEntry)entry).Path;
        FileExists = File.Exists(Path);
    }

    partial void OnPathChanged(string value) => Update<BitmapEntry>("bitmap", e => e with { Path = value.Trim() });

    [RelayCommand]
    private async Task BrowseAsync()
    {
        var folder = File.Exists(Path) ? System.IO.Path.GetDirectoryName(Path) : null;
        if (await _interaction.PickBitmapAsync(folder) is { } picked)
        {
            Path = picked;
            FileExists = File.Exists(picked);
        }
    }
}

/// <summary>Part-specific graphic slot.</summary>
public sealed partial class LabelGraphicPropertiesViewModel : EntryPropertiesViewModel
{
    public LabelGraphicPropertiesViewModel(EditorSession session, LabelGraphicEntry entry)
        : base(session, entry) => Refresh(entry);

    public override string Title => "Label graphic";

    public IReadOnlyList<string> Slots { get; } = ["Draw 1", "Draw 2", "Draw 3", "Draw 4"];

    [ObservableProperty]
    private int _graphicIndex;

    protected override void OnRefresh(LabelEntry entry) => GraphicIndex = ((LabelGraphicEntry)entry).GraphicIndex;

    partial void OnGraphicIndexChanged(int value) => Update<LabelGraphicEntry>("graphic", e => e with { GraphicIndex = Math.Max(0, value) });
}

/// <summary>Corner / edge logo.</summary>
public sealed partial class CornerLogoPropertiesViewModel : EntryPropertiesViewModel
{
    public CornerLogoPropertiesViewModel(EditorSession session, CornerLogoEntry entry)
        : base(session, entry) => Refresh(entry);

    public override string Title => "Edges (corner logo)";

    public IReadOnlyList<string> EdgeInfoOptions { get; } = ["Material", "Color", "Type"];

    public string Hint => "The part with its edge banding. When printing, each side shows the part's edge data; "
        + "the texts below are samples for the design. Leave a side empty for no edge there.";

    /// <summary>Part data shown on each side for the chosen edge info, e.g. "MatEdgeUp".</summary>
    public string TopField => Fields.Top;

    public string BottomField => Fields.Bottom;

    public string LeftField => Fields.Left;

    public string RightField => Fields.Right;

    private (string Top, string Bottom, string Left, string Right) Fields =>
        new CornerLogoEntry { Id = Id, Bounds = default, EdgeInfo = EdgeInfo }.EdgeFieldNames;

    /// <summary>Choices for <see cref="OrientationIndex"/>: not set (older LEdit files), then LEdit's radio buttons.</summary>
    public IReadOnlyList<string> OrientationOptions { get; } = ["(not set)", "0°", "+90°", "−90°", "180°"];

    /// <summary>Index into <see cref="OrientationOptions"/>: LEdit's Orient value + 1, 0 when the file has none.</summary>
    [ObservableProperty]
    private int _orientationIndex;

    [ObservableProperty]
    private bool _keepAspectRatio;

    [ObservableProperty]
    private bool _printEdgeMaterial;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TopField), nameof(BottomField), nameof(LeftField), nameof(RightField))]
    private int _edgeInfo;

    [ObservableProperty]
    private decimal? _edgeThicknessPercent;

    [ObservableProperty]
    private string _textTop = string.Empty;

    [ObservableProperty]
    private string _textBottom = string.Empty;

    [ObservableProperty]
    private string _textLeft = string.Empty;

    [ObservableProperty]
    private string _textRight = string.Empty;

    protected override void OnRefresh(LabelEntry entry)
    {
        var c = (CornerLogoEntry)entry;
        KeepAspectRatio = c.KeepAspectRatio;
        PrintEdgeMaterial = c.PrintEdgeMaterial;
        EdgeInfo = c.EdgeInfo;
        EdgeThicknessPercent = c.EdgeThicknessPercent;
        OrientationIndex = c.Orientation is { } o ? o + 1 : 0;
        TextTop = c.TextTop;
        TextBottom = c.TextBottom;
        TextLeft = c.TextLeft;
        TextRight = c.TextRight;
    }

    partial void OnKeepAspectRatioChanged(bool value) => Update<CornerLogoEntry>("aspect ratio", e => e with { KeepAspectRatio = value });

    partial void OnPrintEdgeMaterialChanged(bool value) => Update<CornerLogoEntry>("edge printing", e => e with { PrintEdgeMaterial = value });

    partial void OnEdgeInfoChanged(int value) => Update<CornerLogoEntry>("edge info", e => e with { EdgeInfo = Math.Max(0, value) });

    partial void OnEdgeThicknessPercentChanged(decimal? value) => Update<CornerLogoEntry>("edge thickness", e => e with { EdgeThicknessPercent = (int)Math.Clamp(value ?? 0, 0, 50) });

    partial void OnOrientationIndexChanged(int value)
    {
        if (value >= 0)
        {
            Update<CornerLogoEntry>("orientation", e => e with { Orientation = value == 0 ? null : value - 1 });
        }
    }

    partial void OnTextTopChanged(string value) => Update<CornerLogoEntry>("edge texts", e => e with { TextTop = value });

    partial void OnTextBottomChanged(string value) => Update<CornerLogoEntry>("edge texts", e => e with { TextBottom = value });

    partial void OnTextLeftChanged(string value) => Update<CornerLogoEntry>("edge texts", e => e with { TextLeft = value });

    partial void OnTextRightChanged(string value) => Update<CornerLogoEntry>("edge texts", e => e with { TextRight = value });
}

/// <summary>Entry the editor cannot interpret; only its position can change.</summary>
public sealed class UnknownEntryPropertiesViewModel : EntryPropertiesViewModel
{
    public UnknownEntryPropertiesViewModel(EditorSession session, UnknownEntry entry)
        : base(session, entry)
    {
        ElementName = entry.ElementName;
        Refresh(entry);
    }

    public override string Title => $"Unknown entry <{ElementName}>";

    public string ElementName { get; }

    public string Hint => "Kept unchanged when saving.";

    protected override void OnRefresh(LabelEntry entry)
    {
    }
}
