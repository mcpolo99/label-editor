using System.Text;
using System.Xml.Linq;

namespace LabelEditor.Infrastructure.Ledit;

/// <summary>
/// Serializes an element tree the way LEdit (MSXML) does: <c>&lt;Field a="1"/&gt;</c> without a space before
/// the slash, CDATA kept as CDATA. <see cref="System.Xml.XmlWriter"/> cannot be configured to match, and
/// matching makes saved files byte-identical to LEdit's, which keeps diffs and reviews meaningful.
/// </summary>
internal static class LeditXmlEmitter
{
    public static string Emit(XElement root, bool indented, string newLine)
    {
        var builder = new StringBuilder(16 * 1024);
        WriteElement(builder, root, indented, newLine, depth: 0);
        return builder.ToString();
    }

    private static void WriteElement(StringBuilder b, XElement element, bool indented, string newLine, int depth)
    {
        b.Append('<').Append(element.Name.LocalName);
        foreach (var attribute in element.Attributes())
        {
            b.Append(' ').Append(attribute.Name.LocalName).Append("=\"");
            Escape(b, attribute.Value, isAttribute: true);
            b.Append('"');
        }

        var nodes = element.Nodes().ToList();
        if (nodes.Count == 0)
        {
            b.Append("/>");
            return;
        }

        b.Append('>');
        var elementOnly = nodes.TrueForAll(n => n is XElement);
        foreach (var node in nodes)
        {
            switch (node)
            {
                case XElement child:
                    if (indented && elementOnly)
                    {
                        b.Append(newLine).Append('\t', depth + 1);
                    }

                    WriteElement(b, child, indented, newLine, depth + 1);
                    break;
                case XCData cdata:
                    b.Append("<![CDATA[").Append(cdata.Value.Replace("]]>", "]]]]><![CDATA[>", StringComparison.Ordinal)).Append("]]>");
                    break;
                case XText text:
                    Escape(b, text.Value, isAttribute: false);
                    break;
                case XComment comment:
                    b.Append("<!--").Append(comment.Value).Append("-->");
                    break;
            }
        }

        if (indented && elementOnly)
        {
            b.Append(newLine).Append('\t', depth);
        }

        b.Append("</").Append(element.Name.LocalName).Append('>');
    }

    private static void Escape(StringBuilder b, string value, bool isAttribute)
    {
        foreach (var c in value)
        {
            switch (c)
            {
                case '&': b.Append("&amp;"); break;
                case '<': b.Append("&lt;"); break;
                case '>': b.Append("&gt;"); break;
                case '"' when isAttribute: b.Append("&quot;"); break;
                case '\n' when isAttribute: b.Append("&#10;"); break;
                case '\r' when isAttribute: b.Append("&#13;"); break;
                case '\t' when isAttribute: b.Append("&#9;"); break;
                default:
                    // Outside ISO-8859-1: use a character reference so the file stays valid.
                    if (c > 'ÿ')
                    {
                        b.Append("&#").Append((int)c).Append(';');
                    }
                    else
                    {
                        b.Append(c);
                    }

                    break;
            }
        }
    }
}
