using System.Globalization;
using System.Text;
using LabelEditor.Domain.Entries;

namespace LabelEditor.Domain.Text;

/// <summary>
/// Builds the printed string of a text entry: concatenates elements and applies the per-element
/// trimming rules plus the entry-level upper-case option.
/// </summary>
public static class TextComposer
{
    /// <param name="entry">Text entry to compose.</param>
    /// <param name="fieldValue">Supplies the value of a data field element; null falls back to the element preview text.</param>
    /// <returns>The text as it would print.</returns>
    public static string Compose(TextEntry entry, Func<DataFieldReference, TextElement, string?> fieldValue)
    {
        var builder = new StringBuilder();
        foreach (var element in entry.Elements)
        {
            var raw = element.Source switch
            {
                LiteralText literal => literal.Text,
                DataFieldReference field => fieldValue(field, element) ?? element.PreviewText ?? string.Empty,
                _ => string.Empty,
            };

            builder.Append(ApplyTrimming(raw, element));
        }

        var text = builder.ToString();
        return entry.Options.UpperCase ? text.ToUpper(CultureInfo.CurrentCulture) : text;
    }

    /// <summary>Applies First/DeleteFirst, DeleteLast and Count to one element value.</summary>
    public static string ApplyTrimming(string value, TextElement element)
    {
        var start = Math.Max(0, (element.DeleteFirst ?? 0) + (element.First ?? 0));
        var end = value.Length - Math.Max(0, element.DeleteLast ?? 0);
        if (start >= end)
        {
            return string.Empty;
        }

        var length = end - start;
        if (element.Count >= 0)
        {
            length = Math.Min(length, element.Count);
        }

        return value.Substring(start, length);
    }
}
