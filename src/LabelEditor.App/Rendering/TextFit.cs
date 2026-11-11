using System.Globalization;
using Avalonia;
using Avalonia.Media;
using LabelEditor.Application.Preview;
using LabelEditor.Domain.Entries;
using LabelEditor.Domain.Layout;
using LabelEditor.Domain.Text;
using LabelEditor.Presentation.Services;
using LabelEditor.Rendering.Scene;

namespace LabelEditor.App.Rendering;

/// <summary>Whether a text item fits its field at print size, measured with the same fonts the painter uses.</summary>
public static class TextFit
{
    /// <summary>Overflow below this share of the font size is ignored (glyph overhang, rounding).</summary>
    private const double Tolerance = 0.05;

    /// <summary>Size of the text's field in its own direction (width along the text).</summary>
    public static Size Frame(TextItem t) =>
        t.Rotation is TextRotation.Ccw90 or TextRotation.Cw90 ? new Size(t.Bounds.Height, t.Bounds.Width) : new Size(t.Bounds.Width, t.Bounds.Height);

    /// <summary>
    /// True when the text is wider than its field (word wrap: taller). Texts that scale to fill their field
    /// (font size not fixed) always fit, and design-view placeholders ("[FieldName]") are not judged.
    /// </summary>
    public static bool Overflows(TextItem t)
    {
        if (t.EmSize is not { } em || t.Text.Length == 0 || t.HasPlaceholder)
        {
            return false;
        }

        var frame = Frame(t);
        var typeface = ScenePainter.TypefaceFor(t.FontFamily, t.Bold, t.Italic);
        FormattedText Measure(string value) => new(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, em, Brushes.Black);
        var text = Measure(t.Text);
        if (t.WordWrap)
        {
            // One line may be taller than the field (LEdit centres it); a further line that does not fit is overflow.
            text.MaxTextWidth = Math.Max(1, frame.Width);
            var lineHeight = Measure("X").Height;
            return text.Height > Math.Max(frame.Height, lineHeight) + (lineHeight * 0.5);
        }

        return text.WidthIncludingTrailingWhitespace - frame.Width > em * Tolerance;
    }
}

/// <summary><see cref="ITextOverflowCheck"/> using <see cref="TextFit"/>.</summary>
public sealed class AvaloniaTextOverflowCheck : ITextOverflowCheck
{
    public IReadOnlyList<TextOverflow> Find(LabelLayout layout, FieldTextResolver resolver, IReadOnlyList<PreviewRecord> records)
    {
        var builder = new LabelSceneBuilder(resolver);
        var found = new Dictionary<EntryId, TextOverflow>();
        foreach (var record in records.Cast<PreviewRecord?>().Prepend(null))
        {
            foreach (var item in builder.Build(layout, record).Items.OfType<TextItem>())
            {
                if (!found.ContainsKey(item.Id) && TextFit.Overflows(item))
                {
                    found[item.Id] = new TextOverflow(item.Id, item.Text, record?.Name);
                }
            }
        }

        // In label order, so the list reads like the label.
        return [.. layout.Entries.Where(e => found.ContainsKey(e.Id)).Select(e => found[e.Id])];
    }
}
