namespace LabelEditor.Domain.Text;

/// <summary>What a text element prints: either fixed text or the value of an NC data field.</summary>
public abstract record ElementSource;

/// <summary>Fixed text typed by the user ("Raw text" in LEdit).</summary>
public sealed record LiteralText(string Text) : ElementSource
{
    public override string ToString() => $"\"{Text}\"";
}

/// <summary>The value of an NC data field, identified by its LEdit field id (e.g. 45804 = finished length).</summary>
public sealed record DataFieldReference(int FieldId) : ElementSource
{
    public override string ToString() => FieldId.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// One piece of a text entry. A text entry concatenates its elements, e.g. <c>"L: "</c> + [FinalLength].
/// Optional members are null when the file did not contain them (LEdit 1.32 used <see cref="First"/>,
/// 1.34 uses <see cref="DeleteFirst"/>/<see cref="DeleteLast"/>).
/// </summary>
public sealed record TextElement
{
    public required ElementSource Source { get; init; }

    /// <summary>Sample value shown while designing (<c>PrvTxt</c>).</summary>
    public string? PreviewText { get; init; }

    /// <summary>Characters at the start not to print.</summary>
    public int? DeleteFirst { get; init; } = 0;

    /// <summary>Characters at the end not to print.</summary>
    public int? DeleteLast { get; init; } = 0;

    /// <summary>Legacy (v1.32) start offset.</summary>
    public int? First { get; init; }

    /// <summary>Number of characters to print; -1 = all.</summary>
    public int Count { get; init; } = -1;

    /// <summary>Raw <c>FillChar</c> value; see <see cref="FillCharacterMode"/>.</summary>
    public int? FillCharacter { get; init; } = 0;

    public static TextElement ForField(int fieldId, string? preview = null) => new() { Source = new DataFieldReference(fieldId), PreviewText = preview };

    public static TextElement ForLiteral(string text) => new() { Source = new LiteralText(text), FillCharacter = -1 };
}
