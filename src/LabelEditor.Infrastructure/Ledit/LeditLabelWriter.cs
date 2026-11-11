using System.Xml.Linq;
using LabelEditor.Application.Abstractions;
using LabelEditor.Domain.Entries;
using LabelEditor.Domain.Geometry;
using LabelEditor.Domain.Layout;
using LabelEditor.Domain.Text;
using static LabelEditor.Infrastructure.Ledit.LeditNames;

namespace LabelEditor.Infrastructure.Ledit;

/// <summary>Maps the domain model to LEdit label XML, reproducing LEdit's element order and number formats.</summary>
internal static class LeditLabelWriter
{
    public static XElement Write(LabelLayout layout) =>
        new(
            Root,
            WriteHeader(layout.Header),
            new XElement(Entries, new XAttribute("description", EntriesDescription), layout.Entries.Select(WriteEntry)));

    private static XElement WriteHeader(LabelHeader h) =>
        new(
            Header,
            new XAttribute("description", HeaderDescription),
            new XElement(VersionTag, new XAttribute("majorVersion", Format(h.Version.Major)), new XAttribute("minorVersion", Format(h.Version.Minor))),
            new XElement(
                Miscellaneous,
                new XAttribute("default", Format(h.DefaultFlag)),
                new XAttribute("unit", Format((int)h.Unit)),
                new XAttribute("grid", Format(h.GridSize))),
            new XElement(
                RectLabel,
                new XAttribute("type", Format(h.Shape.ShapeType)),
                Cdata("Width", Format(h.Shape.Width)),
                Cdata("Height", Format(-h.Shape.Height))));

    private static XElement WriteEntry(LabelEntry entry) => entry switch
    {
        TextEntry t => Typed(
            TextEntryTag,
            t,
            WriteFont(t.Font),
            WriteOptions(t.Options),
            t.Elements.Select(WriteElement)),
        EllipseEntry el => Typed(
            EllipseEntryTag,
            el,
            Cdata("PenWidth", Format(el.PenWidth)),
            Cdata("PenStyle", Format(el.PenStyle)),
            Cdata("PenColor", el.PenColor.ToString()),
            Cdata("BrushColor", el.BrushColor.ToString())),
        LineEntry l => Typed(LineEntryTag, l),
        BitmapEntry b => Typed(BitmapEntryTag, b, Cdata("Path", b.Path)),
        LabelGraphicEntry g => Typed(LabelGraphicTag, g, Cdata("GraphicIndex", Format(g.GraphicIndex))),
        CornerLogoEntry c => Typed(
            CornerLogoEntryTag,
            c,
            new XElement(
                "CornerLogoData",
                new XAttribute("ImageProp", Flag(c.KeepAspectRatio)),
                new XAttribute("PrnMatEdge", Flag(c.PrintEdgeMaterial)),
                new XAttribute("EdgeInfo", Format(c.EdgeInfo)),
                new XAttribute("TextT", c.TextTop),
                new XAttribute("TextB", c.TextBottom),
                new XAttribute("TextL", c.TextLeft),
                new XAttribute("TextR", c.TextRight)),
            Cdata("EdgeThickDisplay", Format(c.EdgeThicknessPercent))),
        DataRecordEntry d => Typed(DataRecordTag, d, WriteFont(d.Font)),
        UnknownEntry u => XElement.Parse(u.OpaqueContent),
        _ => throw new LabelDocumentException($"Entry kind {entry.Kind} cannot be written."),
    };

    private static XElement Typed(string name, LabelEntry entry, params object[] content) =>
        new(name, new XAttribute("type", Format((int)entry.Kind)), WriteField(entry.Bounds), content);

    private static XElement WriteField(LabelRect b) =>
        new(
            Field,
            new XAttribute("left", FormatFieldNumber(b.X)),
            new XAttribute("top", FormatFieldNumber(-b.Y)),
            new XAttribute("width", FormatFieldNumber(b.Width)),
            new XAttribute("height", FormatFieldNumber(-b.Height)));

    private static XElement WriteFont(FontSpec f) =>
        new(
            Font,
            Cdata("Height", Format(f.Height)),
            Cdata("Width", Format(f.Width)),
            Cdata("Escapement", Format(f.Escapement)),
            Cdata("Orientation", Format(f.Orientation)),
            Cdata("Weight", Format(f.Weight)),
            Cdata("Italic", Format(f.Italic)),
            Cdata("Underline", Format(f.Underline)),
            Cdata("StrikeOut", Format(f.StrikeOut)),
            Cdata("CharSet", Format(f.CharSet)),
            Cdata("OutPrecision", Format(f.OutPrecision)),
            Cdata("ClipPrecision", Format(f.ClipPrecision)),
            Cdata("Quality", Format(f.Quality)),
            Cdata("PitchAndFamily", Format(f.PitchAndFamily)),
            Cdata("FaceName", f.FaceName));

    private static XElement WriteOptions(TextOptions o)
    {
        var element = new XElement(
            FontOptions,
            new XAttribute("FontSizeFixed", Flag(o.FontSizeFixed)),
            new XAttribute("FontSize", Format(o.FontSize)),
            new XAttribute("UpperCase", Flag(o.UpperCase)),
            new XAttribute("BarcodeSettings", Format(o.Barcode.Raw)));
        AddOptional(element, "TextColor", o.TextColor);
        AddOptional(element, "CodePage", o.CodePage);
        AddOptional(element, "Multiline", o.Multiline is { } m ? (m ? 1 : 0) : null);
        return element;
    }

    private static XElement WriteElement(TextElement e, int index)
    {
        var element = new XElement(Element, new XAttribute("index", Format(index)));
        if (e.PreviewText is not null)
        {
            element.Add(new XAttribute("PrvTxt", e.PreviewText));
        }

        element.Add(Cdata("ElementID", e.Source switch
        {
            LiteralText literal => $"\"{literal.Text}\"",
            DataFieldReference field => Format(field.FieldId),
            _ => throw new LabelDocumentException("Unknown text element source."),
        }));
        AddOptionalChild(element, "DelFirst", e.DeleteFirst);
        AddOptionalChild(element, "DelLast", e.DeleteLast);
        AddOptionalChild(element, "First", e.First);
        element.Add(Cdata("Count", Format(e.Count)));
        AddOptionalChild(element, "FillChar", e.FillCharacter);
        return element;
    }

    private static void AddOptional(XElement element, string name, int? value)
    {
        if (value is { } v)
        {
            element.Add(new XAttribute(name, Format(v)));
        }
    }

    private static void AddOptionalChild(XElement element, string name, int? value)
    {
        if (value is { } v)
        {
            element.Add(Cdata(name, Format(v)));
        }
    }

    private static string Flag(bool value) => value ? "1" : "0";

    /// <summary>LEdit wraps every element value in CDATA. CDATA cannot escape, so check the encoding up front.</summary>
    private static XElement Cdata(string name, string value)
    {
        foreach (var c in value)
        {
            if (c > 'ÿ')
            {
                throw new LabelDocumentException($"'{value}' contains the character '{c}', which LEdit label files (ISO-8859-1) cannot store.");
            }
        }

        return new XElement(name, new XCData(value));
    }
}
