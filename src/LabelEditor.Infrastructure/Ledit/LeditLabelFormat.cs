using System.Xml;
using System.Xml.Linq;
using LabelEditor.Application.Abstractions;
using LabelEditor.Domain.Layout;

namespace LabelEditor.Infrastructure.Ledit;

/// <summary>Output layout of saved label files.</summary>
public enum LeditXmlStyle
{
    /// <summary>Exactly as LEdit writes: declaration, CRLF, the whole document on one line, CRLF.</summary>
    Native,

    /// <summary>Indented with tabs; easier to diff. LEdit reads it fine.</summary>
    Indented,
}

/// <summary>LEdit label XML (<c>MixedData/Label/*.xml</c>).</summary>
public sealed class LeditLabelFormat(LeditXmlStyle style = LeditXmlStyle.Native) : ILabelLayoutFormat
{
    private const string NewLine = "\r\n";

    public async Task<LabelLayout> ReadAsync(Stream source, CancellationToken cancellationToken = default)
    {
        XDocument document;
        try
        {
            // The declaration names ISO-8859-1; XmlReader honours it.
            document = await XDocument.LoadAsync(source, LoadOptions.None, cancellationToken).ConfigureAwait(false);
        }
        catch (XmlException ex)
        {
            throw new LabelDocumentException($"The file is not valid XML: {ex.Message}", ex);
        }

        return LeditLabelReader.Read(document);
    }

    public async Task WriteAsync(LabelLayout layout, Stream destination, CancellationToken cancellationToken = default)
    {
        var bytes = Serialize(layout);
        await destination.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The complete file content. Building it fully in memory means validation errors surface before anything is written.</summary>
    public byte[] Serialize(LabelLayout layout)
    {
        var body = LeditXmlEmitter.Emit(LeditLabelWriter.Write(layout), style == LeditXmlStyle.Indented, NewLine);
        return LeditNames.FileEncoding.GetBytes(LeditNames.XmlDeclaration + NewLine + body + NewLine);
    }
}
