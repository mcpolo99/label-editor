using System.Globalization;
using System.Xml.Linq;
using LabelEditor.Application.Abstractions;
using LabelEditor.Domain.Common;
using LabelEditor.Domain.Entries;
using LabelEditor.Domain.Geometry;
using LabelEditor.Domain.Layout;
using LabelEditor.Domain.Text;
using static LabelEditor.Infrastructure.Ledit.LeditNames;

namespace LabelEditor.Infrastructure.Ledit;

/// <summary>Maps an LEdit label XML document to the domain model.</summary>
/// <remarks>
/// LEdit stores Y upwards (top and height are negative for anything below the label's top edge);
/// the domain uses Y downwards, so both are negated here and again on write.
/// </remarks>
internal static class LeditLabelReader
{
    public static LabelLayout Read(XDocument document)
    {
        var root = document.Root;
        if (root is null || root.Name.LocalName != Root)
        {
            throw new LabelDocumentException($"Not an LEdit label layout: root element is '{root?.Name.LocalName ?? "(none)"}', expected '{Root}'.");
        }

        var header = ReadHeader(root.Element(Header));
        var entries = root.Element(Entries)?.Elements().Select(ReadEntry).ToValueList() ?? [];
        return new LabelLayout { Header = header, Entries = entries };
    }

    private static LabelHeader ReadHeader(XElement? header)
    {
        if (header is null)
        {
            return new LabelHeader();
        }

        var version = header.Element(VersionTag);
        var misc = header.Element(Miscellaneous);
        var rect = header.Element(RectLabel);
        return new LabelHeader
        {
            Version = new FormatVersion(IntAttr(version, "majorVersion", 1), IntAttr(version, "minorVersion", 34)),
            DefaultFlag = IntAttr(misc, "default", 0),
            Unit = (MeasurementUnit)IntAttr(misc, "unit", 0),
            GridSize = IntAttr(misc, "grid", 0),
            Shape = new LabelShape
            {
                ShapeType = IntAttr(rect, "type", 1),
                Width = Math.Abs(IntChild(rect, "Width", 100_000)),
                Height = Math.Abs(IntChild(rect, "Height", -60_000)),
            },
        };
    }

    private static LabelEntry ReadEntry(XElement e)
    {
        var id = EntryId.New();
        var bounds = ReadField(e.Element(Field));
        return e.Name.LocalName switch
        {
            TextEntryTag => new TextEntry
            {
                Id = id,
                Bounds = bounds,
                Font = ReadFont(e.Element(Font)),
                Options = ReadOptions(e.Element(FontOptions)),
                Elements = e.Elements(Element).Select(ReadElement).ToValueList(),
            },
            EllipseEntryTag => new EllipseEntry
            {
                Id = id,
                Bounds = bounds,
                PenWidth = IntChild(e, "PenWidth", 1),
                PenStyle = IntChild(e, "PenStyle", 0),
                PenColor = ParseColor(Child(e, "PenColor"), RgbColor.Black),
                BrushColor = ParseColor(Child(e, "BrushColor"), RgbColor.White),
            },
            LineEntryTag => new LineEntry { Id = id, Bounds = bounds },
            BitmapEntryTag => new BitmapEntry { Id = id, Bounds = bounds, Path = Child(e, "Path") ?? string.Empty },
            LabelGraphicTag => new LabelGraphicEntry { Id = id, Bounds = bounds, GraphicIndex = IntChild(e, "GraphicIndex", 0) },
            CornerLogoEntryTag => ReadCornerLogo(e, id, bounds),
            DataRecordTag => new DataRecordEntry { Id = id, Bounds = bounds, Font = ReadFont(e.Element(Font)) },
            _ => new UnknownEntry
            {
                Id = id,
                Bounds = bounds,
                ElementName = e.Name.LocalName,
                TypeCode = IntAttr(e, "type", 0),
                OpaqueContent = e.ToString(SaveOptions.DisableFormatting),
            },
        };
    }

    private static CornerLogoEntry ReadCornerLogo(XElement e, EntryId id, LabelRect bounds)
    {
        var data = e.Element("CornerLogoData");
        var defaults = new CornerLogoEntry { Id = id, Bounds = bounds };
        return defaults with
        {
            KeepAspectRatio = IntAttr(data, "ImageProp", 1) != 0,
            PrintEdgeMaterial = IntAttr(data, "PrnMatEdge", 1) != 0,
            EdgeInfo = IntAttr(data, "EdgeInfo", 0),
            Orientation = data?.Attribute("Orient") is { } orient && int.TryParse(orient.Value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var o) ? o : null,

            // LEdit leaves out the text of a side without an edge.
            TextTop = data?.Attribute("TextT")?.Value ?? string.Empty,
            TextBottom = data?.Attribute("TextB")?.Value ?? string.Empty,
            TextLeft = data?.Attribute("TextL")?.Value ?? string.Empty,
            TextRight = data?.Attribute("TextR")?.Value ?? string.Empty,
            EdgeThicknessPercent = IntChild(e, "EdgeThickDisplay", defaults.EdgeThicknessPercent),
        };
    }

    private static LabelRect ReadField(XElement? field) => field is null
        ? default
        : new LabelRect(IntAttr(field, "left", 0), -IntAttr(field, "top", 0), IntAttr(field, "width", 0), -IntAttr(field, "height", 0));

    private static FontSpec ReadFont(XElement? font) => font is null
        ? FontSpec.Default
        : new FontSpec
        {
            Height = IntChild(font, "Height", 0),
            Width = IntChild(font, "Width", 0),
            Escapement = IntChild(font, "Escapement", 0),
            Orientation = IntChild(font, "Orientation", 0),
            Weight = IntChild(font, "Weight", 0),
            Italic = IntChild(font, "Italic", 0),
            Underline = IntChild(font, "Underline", 0),
            StrikeOut = IntChild(font, "StrikeOut", 0),
            CharSet = IntChild(font, "CharSet", 0),
            OutPrecision = IntChild(font, "OutPrecision", 0),
            ClipPrecision = IntChild(font, "ClipPrecision", 0),
            Quality = IntChild(font, "Quality", 0),
            PitchAndFamily = IntChild(font, "PitchAndFamily", 0),
            FaceName = Child(font, "FaceName") ?? "Arial",
        };

    private static TextOptions ReadOptions(XElement? o) => o is null
        ? new TextOptions()
        : new TextOptions
        {
            FontSizeFixed = IntAttr(o, "FontSizeFixed", 1) != 0,
            FontSize = IntAttr(o, "FontSize", 100),
            UpperCase = IntAttr(o, "UpperCase", 0) != 0,
            Barcode = new BarcodeSettings(IntAttr(o, "BarcodeSettings", 0)),
            TextColor = OptionalIntAttr(o, "TextColor"),
            CodePage = OptionalIntAttr(o, "CodePage"),
            Multiline = OptionalIntAttr(o, "Multiline") is { } m ? m != 0 : null,
        };

    private static TextElement ReadElement(XElement e)
    {
        var rawId = (Child(e, "ElementID") ?? string.Empty).Trim();
        ElementSource source = rawId.Length >= 2 && rawId[0] == '"' && rawId[^1] == '"'
            ? new LiteralText(rawId[1..^1])
            : int.TryParse(rawId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var fieldId)
                ? new DataFieldReference(fieldId)
                : new LiteralText(rawId);

        return new TextElement
        {
            Source = source,
            PreviewText = e.Attribute("PrvTxt")?.Value,
            DeleteFirst = OptionalIntChild(e, "DelFirst"),
            DeleteLast = OptionalIntChild(e, "DelLast"),
            First = OptionalIntChild(e, "First"),
            Count = IntChild(e, "Count", -1),
            FillCharacter = OptionalIntChild(e, "FillChar"),
        };
    }

    private static RgbColor ParseColor(string? value, RgbColor fallback)
    {
        var parts = value?.Split(',', StringSplitOptions.TrimEntries);
        return parts is { Length: 3 }
            && byte.TryParse(parts[0], CultureInfo.InvariantCulture, out var r)
            && byte.TryParse(parts[1], CultureInfo.InvariantCulture, out var g)
            && byte.TryParse(parts[2], CultureInfo.InvariantCulture, out var b)
                ? new RgbColor(r, g, b)
                : fallback;
    }

    private static string? Child(XElement? parent, string name) => parent?.Element(name)?.Value;

    private static int IntChild(XElement? parent, string name, int fallback) => OptionalIntChild(parent, name) ?? fallback;

    private static int? OptionalIntChild(XElement? parent, string name) => ParseInt(Child(parent, name));

    private static int IntAttr(XElement? element, string name, int fallback) => OptionalIntAttr(element, name) ?? fallback;

    private static int? OptionalIntAttr(XElement? element, string name) => ParseInt(element?.Attribute(name)?.Value);

    private static int? ParseInt(string? value) =>
        int.TryParse(value?.Trim(), NumberStyles.AllowLeadingSign | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;
}
