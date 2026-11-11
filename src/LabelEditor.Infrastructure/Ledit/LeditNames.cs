using System.Globalization;
using System.Text;

namespace LabelEditor.Infrastructure.Ledit;

/// <summary>Element and attribute names of the LEdit label XML format.</summary>
internal static class LeditNames
{
    public const string Root = "LabelLayout";
    public const string Header = "Header";
    public const string HeaderDescription = "General information";
    public const string VersionTag = "Version";
    public const string Miscellaneous = "Miscellaneous";
    public const string RectLabel = "RectLabel";
    public const string Entries = "Entries";
    public const string EntriesDescription = "Label elements";
    public const string Field = "Field";
    public const string Font = "Font";
    public const string FontOptions = "FontOptions";
    public const string Element = "Element";

    public const string TextEntryTag = "TextEntry";
    public const string BitmapEntryTag = "BitmapEntry";
    public const string LabelGraphicTag = "LabelGraphic";
    public const string LineEntryTag = "LineEntry";
    public const string EllipseEntryTag = "EllipseEntry";
    public const string CornerLogoEntryTag = "CornerLogoEntry";
    public const string DataRecordTag = "DataRecord";

    /// <summary>Element added by the old HTML editor; ignored on read and never written.</summary>
    public const string HtmlEditorTimestamp = "Timestamp";

    /// <summary>Label files are ISO-8859-1, as written by LEdit.</summary>
    public static Encoding FileEncoding => Encoding.Latin1;

    public const string XmlDeclaration = "<?xml version=\"1.0\" encoding=\"ISO-8859-1\"?>";

    /// <summary>
    /// LEdit writes Field coordinates as 8 characters including the sign: 00084000, -0014000.
    /// </summary>
    public static string FormatFieldNumber(int value) =>
        value < 0
            ? "-" + Math.Abs((long)value).ToString(CultureInfo.InvariantCulture).PadLeft(7, '0')
            : value.ToString(CultureInfo.InvariantCulture).PadLeft(8, '0');

    public static string Format(int value) => value.ToString(CultureInfo.InvariantCulture);
}
