using System.Text.Json;
using System.Text.Json.Serialization;
using LabelEditor.Domain.Entries;
using LabelEditor.Domain.Layout;

namespace LabelEditor.Infrastructure.Storage;

/// <summary>
/// Editor-only settings (e.g. size locks) kept in <c>&lt;label&gt;.xml.labeleditor.json</c> beside the label, so the
/// LEdit file itself stays exactly what LEdit and the machines expect.
/// <para>
/// LEdit entries have no ids, so each record identifies its entry by position plus a fingerprint (kind and size).
/// If the label was changed elsewhere (e.g. in LEdit) and the entry no longer matches, the setting is looked up by
/// fingerprint and otherwise dropped: the failure mode is "no longer locked", never "locks the wrong entry".
/// </para>
/// </summary>
public static class EditorMetadataSidecar
{
    public const string Suffix = ".labeleditor.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string PathFor(string labelPath) => labelPath + Suffix;

    /// <summary>Applies the sidecar of <paramref name="labelPath"/> (if any) to <paramref name="layout"/>.</summary>
    public static async Task<LabelLayout> ApplyAsync(LabelLayout layout, string labelPath, CancellationToken cancellationToken)
    {
        var path = PathFor(labelPath);
        if (!File.Exists(path))
        {
            return layout;
        }

        SidecarFile? file;
        try
        {
            await using var stream = File.OpenRead(path);
            file = await JsonSerializer.DeserializeAsync<SidecarFile>(stream, Options, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return layout; // editor-only data: a broken sidecar must never prevent opening the label
        }

        return file is null ? layout : Apply(layout, file);
    }

    /// <summary>Writes (or removes, when nothing needs keeping) the sidecar for <paramref name="labelPath"/>.</summary>
    public static async Task SaveAsync(LabelLayout layout, string labelPath, CancellationToken cancellationToken)
    {
        var path = PathFor(labelPath);
        var records = layout.Entries
            .Select((entry, index) => (entry, index))
            .Where(x => x.entry.IsSizeLocked)
            .Select(x => new SidecarEntry(x.index, x.entry.Kind, x.entry.Bounds.Width, x.entry.Bounds.Height, SizeLocked: true))
            .ToList();

        if (records.Count == 0)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            return;
        }

        var temp = path + ".tmp";
        await using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
        {
            await JsonSerializer.SerializeAsync(stream, new SidecarFile(1, records), Options, cancellationToken).ConfigureAwait(false);
        }

        File.Move(temp, path, overwrite: true);
    }

    internal static LabelLayout Apply(LabelLayout layout, SidecarFile file)
    {
        var result = layout;
        foreach (var record in file.Entries ?? [])
        {
            var index = Matches(layout, record.Index, record) ? record.Index : FindUnique(layout, record);
            if (index >= 0 && record.SizeLocked)
            {
                result = result.ReplaceEntry(result.Entries[index] with { IsSizeLocked = true });
            }
        }

        return result;
    }

    private static bool Matches(LabelLayout layout, int index, SidecarEntry record) =>
        index >= 0 && index < layout.Entries.Count &&
        layout.Entries[index] is var e && e.Kind == record.Kind && e.Bounds.Width == record.Width && e.Bounds.Height == record.Height;

    private static int FindUnique(LabelLayout layout, SidecarEntry record)
    {
        var candidates = Enumerable.Range(0, layout.Entries.Count).Where(i => Matches(layout, i, record)).ToList();
        return candidates.Count == 1 ? candidates[0] : -1;
    }

    internal sealed record SidecarFile(int Version, IReadOnlyList<SidecarEntry>? Entries);

    internal sealed record SidecarEntry(int Index, EntryKind Kind, int Width, int Height, bool SizeLocked);
}
