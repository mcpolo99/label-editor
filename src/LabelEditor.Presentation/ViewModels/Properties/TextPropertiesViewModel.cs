using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LabelEditor.Application.Documents;
using LabelEditor.Domain.Common;
using LabelEditor.Domain.Entries;
using LabelEditor.Domain.Text;

namespace LabelEditor.Presentation.ViewModels.Properties;

/// <summary>A selectable text direction.</summary>
public sealed record RotationOption(TextRotation Rotation, string Name);

/// <summary>A selectable bar code symbology; <see cref="Type"/> is null for a code the editor does not know.</summary>
public sealed record BarcodeTypeOption(BarcodeType? Type, int Code, string Name);

/// <summary>Text / bar code entry: font, options and the list of elements it prints.</summary>
public sealed partial class TextPropertiesViewModel : EntryPropertiesViewModel
{
    private readonly EditorContext _context;

    public TextPropertiesViewModel(EditorSession session, TextEntry entry, EditorContext context)
        : base(session, entry)
    {
        _context = context;
        Refresh(entry);
    }

    public override string Title => IsBarcode ? "Bar code" : "Text";

    /// <summary>LEdit's bar code fonts first, then installed fonts.</summary>
    public IReadOnlyList<string> FontFamilies => [.. BarcodeFontNames, .. _context.Fonts.FontFamilies];

    /// <summary>C28N = Code 128, A39N = Code 39 (the fonts LEdit pairs with those symbologies).</summary>
    public static IReadOnlyList<string> BarcodeFontNames { get; } = ["C28N", "A39N"];

    public static IReadOnlyList<BarcodeTypeOption> KnownBarcodeTypes { get; } =
    [
        new(Domain.Text.BarcodeType.Code128, (int)Domain.Text.BarcodeType.Code128, "Code 128"),
        new(Domain.Text.BarcodeType.Code128C, (int)Domain.Text.BarcodeType.Code128C, "Code 128 C"),
        new(Domain.Text.BarcodeType.Code39, (int)Domain.Text.BarcodeType.Code39, "Code 39"),
        new(Domain.Text.BarcodeType.QrCode, (int)Domain.Text.BarcodeType.QrCode, "QR code"),
    ];

    /// <summary>Known types, plus the entry's own code when it is not one of them (so it is shown, not lost).</summary>
    [ObservableProperty]
    private IReadOnlyList<BarcodeTypeOption> _barcodeTypes = KnownBarcodeTypes;

    public IReadOnlyList<RotationOption> Rotations { get; } =
    [
        new(TextRotation.None, "0°"),
        new(TextRotation.Ccw90, "+90°"),
        new(TextRotation.Cw90, "-90°"),
        new(TextRotation.Rotate180, "180°"),
    ];

    public ObservableCollection<TextElementViewModel> Elements { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveElementCommand), nameof(MoveElementUpCommand), nameof(MoveElementDownCommand))]
    private TextElementViewModel? _selectedElement;

    [ObservableProperty]
    private string _preview = string.Empty;

    [ObservableProperty]
    private string _fontFamily = string.Empty;

    [ObservableProperty]
    private decimal? _fontSize;

    [ObservableProperty]
    private bool _fontSizeFixed;

    [ObservableProperty]
    private bool _bold;

    [ObservableProperty]
    private bool _italic;

    [ObservableProperty]
    private bool _underline;

    [ObservableProperty]
    private bool _strikeOut;

    [ObservableProperty]
    private RotationOption? _rotation;

    [ObservableProperty]
    private bool _upperCase;

    [ObservableProperty]
    private bool _wordWrap;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    private bool _isBarcode;

    [ObservableProperty]
    private BarcodeTypeOption? _barcodeType;

    protected override void OnRefresh(LabelEntry entry)
    {
        var t = (TextEntry)entry;
        Preview = _context.Resolver.PreviewText(t);
        FontFamily = t.Font.FaceName;
        FontSize = (decimal)t.Options.FontSizePoints;
        FontSizeFixed = t.Options.FontSizeFixed;
        Bold = t.Font.IsBold;
        Italic = t.Font.IsItalic;
        Underline = t.Font.IsUnderline;
        StrikeOut = t.Font.IsStrikeOut;
        Rotation = Rotations.FirstOrDefault(r => r.Rotation == t.Font.Rotation);
        UpperCase = t.Options.UpperCase;
        WordWrap = t.Options.Multiline ?? false;
        IsBarcode = t.Options.Barcode.IsBarcode;
        var code = t.Options.Barcode.SymbologyCode;
        BarcodeTypes = KnownBarcodeTypes.Any(o => o.Code == code) || code == 0
            ? KnownBarcodeTypes
            : [.. KnownBarcodeTypes, new BarcodeTypeOption(null, code, $"Other (code {code})")];
        BarcodeType = BarcodeTypes.FirstOrDefault(o => o.Code == code);
        RefreshElements(t);
    }

    private void RefreshElements(TextEntry entry)
    {
        var selectedIndex = SelectedElement is { } s ? Elements.IndexOf(s) : -1;
        if (Elements.Count != entry.Elements.Count)
        {
            Elements.Clear();
            for (var i = 0; i < entry.Elements.Count; i++)
            {
                Elements.Add(new TextElementViewModel(this, i));
            }
        }

        for (var i = 0; i < entry.Elements.Count; i++)
        {
            Elements[i].Refresh(entry.Elements[i], _context.Resolver);
        }

        SelectedElement = selectedIndex >= 0 && selectedIndex < Elements.Count ? Elements[selectedIndex] : Elements.FirstOrDefault();
    }

    internal void UpdateElement(int index, string property, Func<TextElement, TextElement> change) =>
        Update<TextEntry>($"element {property}", t => index < t.Elements.Count ? t with { Elements = t.Elements.SetItem(index, change(t.Elements[index])) } : t);

    internal bool Refreshing => IsRefreshing;

    /// <summary>Appends a data field (from the field picker).</summary>
    public void AddField(int fieldId) => EditElements("Add field", list => list.Add(TextElement.ForField(fieldId)), selectIndex: int.MaxValue);

    [RelayCommand]
    private void AddText() => EditElements("Add text", list => list.Add(TextElement.ForLiteral("Text")), selectIndex: int.MaxValue);

    [RelayCommand(CanExecute = nameof(HasSelectedElement))]
    private void RemoveElement()
    {
        if (SelectedElement is { } e)
        {
            EditElements("Remove element", list => list.RemoveAt(e.Index), selectIndex: e.Index - 1);
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelectedElement))]
    private void MoveElementUp() => Move(-1);

    [RelayCommand(CanExecute = nameof(HasSelectedElement))]
    private void MoveElementDown() => Move(1);

    private bool HasSelectedElement() => SelectedElement is not null;

    private void Move(int delta)
    {
        if (SelectedElement is not { } e)
        {
            return;
        }

        var target = e.Index + delta;
        EditElements("Reorder elements", list =>
        {
            if (target < 0 || target >= list.Count)
            {
                return list;
            }

            var item = list[e.Index];
            return list.RemoveAt(e.Index).Insert(target, item);
        }, target);
    }

    private void EditElements(string description, Func<ValueList<TextElement>, ValueList<TextElement>> change, int selectIndex)
    {
        Session.Execute(description, l => l.Find(Id) is TextEntry t ? l.ReplaceEntry(t with { Elements = change(t.Elements) }) : l);
        if (Elements.Count > 0)
        {
            SelectedElement = Elements[Math.Clamp(selectIndex, 0, Elements.Count - 1)];
        }
    }

    partial void OnFontFamilyChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            Update<TextEntry>("font", t => t with { Font = t.Font with { FaceName = value.Trim() } });
        }
    }

    partial void OnFontSizeChanged(decimal? value) =>
        Update<TextEntry>("font size", t => t with { Options = t.Options with { FontSize = (int)Math.Round(Math.Clamp(value ?? 10, 1, 500) * 10) } });

    partial void OnFontSizeFixedChanged(bool value) => Update<TextEntry>("fixed size", t => t with { Options = t.Options with { FontSizeFixed = value } });

    partial void OnBoldChanged(bool value) => Update<TextEntry>("bold", t => t with { Font = t.Font.WithBold(value) });

    partial void OnItalicChanged(bool value) => Update<TextEntry>("italic", t => t with { Font = t.Font.WithItalic(value) });

    partial void OnUnderlineChanged(bool value) => Update<TextEntry>("underline", t => t with { Font = t.Font.WithUnderline(value) });

    partial void OnStrikeOutChanged(bool value) => Update<TextEntry>("strike-out", t => t with { Font = t.Font.WithStrikeOut(value) });

    partial void OnRotationChanged(RotationOption? value)
    {
        if (value is not null)
        {
            Update<TextEntry>("rotation", t => t with { Font = t.Font.WithRotation(value.Rotation) });
        }
    }

    partial void OnUpperCaseChanged(bool value) => Update<TextEntry>("upper case", t => t with { Options = t.Options with { UpperCase = value } });

    partial void OnWordWrapChanged(bool value) => Update<TextEntry>("word wrap", t => t with { Options = t.Options with { Multiline = value } });

    partial void OnIsBarcodeChanged(bool value) => Update<TextEntry>("bar code", t => t with
    {
        Options = t.Options with { Barcode = value && t.Options.Barcode.Raw == 0 ? BarcodeSettings.DefaultBarcode : t.Options.Barcode.WithBarcode(value) },
        Font = value && !BarcodeFontNames.Contains(t.Font.FaceName, StringComparer.OrdinalIgnoreCase) ? t.Font with { FaceName = BarcodeFontNames[0] } : t.Font,
    });

    /// <summary>Switching symbology also switches to the font LEdit pairs with it.</summary>
    partial void OnBarcodeTypeChanged(BarcodeTypeOption? value)
    {
        if (value?.Type is not { } type)
        {
            return;
        }

        Update<TextEntry>("bar code type", t => t with
        {
            Options = t.Options with { Barcode = t.Options.Barcode.WithBarcode(true).WithType(type) },
            Font = type switch
            {
                Domain.Text.BarcodeType.Code39 => t.Font with { FaceName = "A39N" },
                Domain.Text.BarcodeType.QrCode => t.Font,
                _ => t.Font with { FaceName = "C28N" },
            },
        });
    }
}

/// <summary>One element of a text entry: fixed text or a data field, with its trimming options.</summary>
public sealed partial class TextElementViewModel(TextPropertiesViewModel owner, int index) : ObservableObject
{
    public int Index { get; } = index;

    public IReadOnlyList<FillCharacterMode> FillModes { get; } = Enum.GetValues<FillCharacterMode>();

    [ObservableProperty]
    private bool _isLiteral;

    /// <summary>Short display: "Text" in quotes, or [FieldName].</summary>
    [ObservableProperty]
    private string _display = string.Empty;

    [ObservableProperty]
    private string _literalText = string.Empty;

    /// <summary>Field element name, e.g. QMin.</summary>
    [ObservableProperty]
    private string _fieldName = string.Empty;

    /// <summary>Translated caption of the field (tooltip).</summary>
    [ObservableProperty]
    private string? _fieldCaption;

    [ObservableProperty]
    private int _fieldId;

    [ObservableProperty]
    private string _previewText = string.Empty;

    [ObservableProperty]
    private decimal? _deleteFirst;

    [ObservableProperty]
    private decimal? _deleteLast;

    [ObservableProperty]
    private decimal? _count;

    [ObservableProperty]
    private FillCharacterMode _fillMode;

    private bool _refreshing;

    internal void Refresh(TextElement element, Application.Preview.FieldTextResolver resolver)
    {
        _refreshing = true;
        try
        {
            IsLiteral = element.Source is LiteralText;
            LiteralText = element.Source is LiteralText l ? l.Text : string.Empty;
            FieldId = element.Source is DataFieldReference f ? f.FieldId : 0;
            FieldName = IsLiteral ? string.Empty : resolver.FieldName(FieldId);
            FieldCaption = IsLiteral ? null : resolver.FieldCaption(FieldId);
            Display = resolver.DescribeElement(element);
            PreviewText = element.PreviewText ?? string.Empty;
            DeleteFirst = element.DeleteFirst ?? element.First ?? 0;
            DeleteLast = element.DeleteLast ?? 0;
            Count = element.Count;
            FillMode = Enum.IsDefined(typeof(FillCharacterMode), element.FillCharacter ?? -1) ? (FillCharacterMode)(element.FillCharacter ?? -1) : FillCharacterMode.NotSet;
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void Update(string property, Func<TextElement, TextElement> change)
    {
        if (!_refreshing && !owner.Refreshing)
        {
            owner.UpdateElement(Index, property, change);
        }
    }

    partial void OnLiteralTextChanged(string value)
    {
        if (IsLiteral)
        {
            Update("text", e => e with { Source = new LiteralText(value) });
        }
    }

    partial void OnPreviewTextChanged(string value) => Update("preview", e => e with { PreviewText = value.Length == 0 ? null : value });

    // Files from LEdit 1.32 use "First" instead of DelFirst; edit whichever the element has.
    partial void OnDeleteFirstChanged(decimal? value) => Update("trim", e => e.DeleteFirst is null && e.First is not null ? e with { First = (int)(value ?? 0) } : e with { DeleteFirst = (int)Math.Max(0, value ?? 0) });

    partial void OnDeleteLastChanged(decimal? value) => Update("trim", e => e with { DeleteLast = (int)Math.Max(0, value ?? 0) });

    partial void OnCountChanged(decimal? value) => Update("count", e => e with { Count = (int)Math.Max(-1, value ?? -1) });

    partial void OnFillModeChanged(FillCharacterMode value) => Update("fill", e => e with { FillCharacter = (int)value });
}
