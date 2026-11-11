using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using LabelEditor.Domain.Entries;
using LabelEditor.Domain.Layout;

namespace LabelEditor.Infrastructure.Storage;

/// <summary>
/// Editor-only data kept in <c>&lt;label&gt;.xml.labeleditor.json</c> beside the label, so the LEdit file itself stays
/// exactly what LEdit and the machines expect:
/// <list type="bullet">
/// <item><b>Entry settings</b> (size locks). LEdit entries have no ids, so each record identifies its entry by position
/// plus a fingerprint (kind and size). If the label was changed elsewhere and the entry no longer matches, the setting
/// is looked up by fingerprint and otherwise dropped: the failure mode is "no longer locked", never "wrong entry".</item>
/// <item><b>Version history</b>: the complete LEdit XML of every saved version, gzip-compressed.</item>
/// </list>
/// </summary>
public static class EditorMetadataSidecar
{
    public const string Suffix = ".labeleditor.json";
    public const int CurrentFormat = 2;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string PathFor(string labelPath) => labelPath + Suffix;

    /// <summary>Reads the sidecar; a missing or unreadable one yields an empty sidecar (editor-only data must never block opening a label).</summary>
    public static async Task<SidecarFile> LoadAsync(string labelPath, CancellationToken cancellationToken)
    {
        var path = PathFor(labelPath);
        if (!File.Exists(path))
        {
            return SidecarFile.Empty;
        }

        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<SidecarFile>(stream, Options, cancellationToken).ConfigureAwait(false) ?? SidecarFile.Empty;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return SidecarFile.Empty;
        }
    }

    /// <summary>Writes the sidecar, or removes it when it holds nothing.</summary>
    public static async Task SaveAsync(SidecarFile sidecar, string labelPath, CancellationToken cancellationToken)
    {
        var path = PathFor(labelPath);
        if ((sidecar.Entries?.Count ?? 0) == 0 && (sidecar.History?.Count ?? 0) == 0)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            return;
        }

        var temp = path + ".tmp";
        await using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 16 * 1024, FileOptions.Asynchronous))
        {
            await JsonSerializer.SerializeAsync(stream, sidecar, Options, cancellationToken).ConfigureAwait(false);
        }

        File.Move(temp, path, overwrite: true);
    }

    /// <summary>Entry settings of <paramref name="layout"/> to persist.</summary>
    public static IReadOnlyList<SidecarEntry> EntrySettingsOf(LabelLayout layout) =>
        layout.Entries
            .Select((entry, index) => (entry, index))
            .Where(x => x.entry.IsSizeLocked)
            .Select(x => new SidecarEntry(x.index, x.entry.Kind, x.entry.Bounds.Width, x.entry.Bounds.Height, SizeLocked: true))
            .ToList();

    /// <summary>Applies stored entry settings to a freshly read layout.</summary>
    public static LabelLayout ApplyEntrySettings(LabelLayout layout, SidecarFile sidecar)
    {
        var result = layout;
        foreach (var record in sidecar.Entries ?? [])
        {
            var index = Matches(layout, record.Index, record) ? record.Index : FindUnique(layout, record);
            if (index >= 0 && record.SizeLocked)
            {
                result = result.ReplaceEntry(result.Entries[index] with { IsSizeLocked = true });
            }
        }

        return result;
    }

    /// <summary>Appends a version unless its content equals the latest one.</summary>
    public static IReadOnlyList<SidecarVersion> AddVersion(IReadOnlyList<SidecarVersion>? history, byte[] content, DateTimeOffset savedAt, string savedBy, string? note)
    {
        var list = (history ?? []).ToList();
        var hash = Hash(content);
        if (list.Count > 0 && list[^1].Sha256 == hash)
        {
            return list;
        }

        var number = list.Count == 0 ? 1 : list.Max(v => v.Number) + 1;
        list.Add(new SidecarVersion(number, savedAt, savedBy, note, content.Length, hash, Compress(content)));
        return list;
    }

    public static byte[] ContentOf(SidecarVersion version)
    {
        using var input = new MemoryStream(Convert.FromBase64String(version.Content));
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return output.ToArray();
    }

    private static string Compress(byte[] content)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            gzip.Write(content);
        }

        return Convert.ToBase64String(output.ToArray());
    }

    private static string Hash(byte[] content) => Convert.ToHexString(SHA256.HashData(content));

    private static bool Matches(LabelLayout layout, int index, SidecarEntry record) =>
        index >= 0 && index < layout.Entries.Count &&
        layout.Entries[index] is var e && e.Kind == record.Kind && e.Bounds.Width == record.Width && e.Bounds.Height == record.Height;

    private static int FindUnique(LabelLayout layout, SidecarEntry record)
    {
        var candidates = Enumerable.Range(0, layout.Entries.Count).Where(i => Matches(layout, i, record)).ToList();
        return candidates.Count == 1 ? candidates[0] : -1;
    }

    /// <summary>The sidecar document. Format 1 had no history; it reads fine as format 2.</summary>
    public sealed record SidecarFile(int Version, IReadOnlyList<SidecarEntry>? Entries, IReadOnlyList<SidecarVersion>? History)
    {
        public static SidecarFile Empty { get; } = new(CurrentFormat, null, null);
    }

    public sealed record SidecarEntry(int Index, EntryKind Kind, int Width, int Height, bool SizeLocked);

    /// <summary>A saved version: <see cref="Content"/> is the LEdit XML, gzip-compressed and base64-encoded.</summary>
    public sealed record SidecarVersion(int Number, DateTimeOffset SavedAt, string SavedBy, string? Note, int Size, string Sha256, string Content);
}
