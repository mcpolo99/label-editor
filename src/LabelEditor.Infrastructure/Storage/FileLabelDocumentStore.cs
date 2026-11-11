using LabelEditor.Application.Abstractions;
using LabelEditor.Domain.Layout;
using LabelEditor.Infrastructure.Ledit;
using static LabelEditor.Infrastructure.Storage.EditorMetadataSidecar;

namespace LabelEditor.Infrastructure.Storage;

/// <summary>
/// Label files on disk, read and written asynchronously; saves are atomic. Editor-only data (entry settings and
/// version history) travels in a sidecar file (<see cref="EditorMetadataSidecar"/>) so the LEdit file stays exactly
/// what LEdit writes.
/// </summary>
public sealed class FileLabelDocumentStore(ILabelLayoutFormat format, TimeProvider? clock = null, string? userName = null)
    : ILabelDocumentStore, ILabelVersionHistory
{
    /// <summary>Note on the version captured from disk before the editor first replaced a label.</summary>
    public const string OriginalNote = "Original (before first save in Label Editor)";

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly string _userName = userName ?? Environment.UserName;

    public async Task<LabelLayout> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        var layout = await ReadLayoutAsync(path, cancellationToken).ConfigureAwait(false);
        var sidecar = await EditorMetadataSidecar.LoadAsync(path, cancellationToken).ConfigureAwait(false);
        return ApplyEntrySettings(layout, sidecar);
    }

    public async Task SaveAsync(LabelLayout layout, string path, CancellationToken cancellationToken = default)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(path)) ?? throw new LabelDocumentException($"Invalid path '{path}'.");
        Directory.CreateDirectory(folder);

        // Serialize once: the same bytes go to the label file and into the history.
        byte[] content;
        using (var buffer = new MemoryStream())
        {
            await format.WriteAsync(layout, buffer, cancellationToken).ConfigureAwait(false);
            content = buffer.ToArray();
        }

        var temp = Path.Combine(folder, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            var sidecar = await EditorMetadataSidecar.LoadAsync(path, cancellationToken).ConfigureAwait(false);
            var history = sidecar.History;

            // First save over a label that has no history yet: keep what was on disk as version 1.
            if ((history?.Count ?? 0) == 0 && File.Exists(path))
            {
                var original = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
                history = AddVersion(history, original, File.GetLastWriteTimeUtc(path), "unknown", OriginalNote);
            }

            history = AddVersion(history, content, _clock.GetUtcNow(), _userName, note: null);

            // Write next to the target, then swap: a crash or full disk never leaves a half-written label.
            await File.WriteAllBytesAsync(temp, content, cancellationToken).ConfigureAwait(false);
            File.Move(temp, path, overwrite: true);
            await EditorMetadataSidecar.SaveAsync(new SidecarFile(CurrentFormat, EntrySettingsOf(layout), history), path, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new LabelDocumentException($"Could not save '{path}': {ex.Message}", ex);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    public async Task<IReadOnlyList<LabelVersionInfo>> GetVersionsAsync(string path, CancellationToken cancellationToken = default)
    {
        var sidecar = await EditorMetadataSidecar.LoadAsync(path, cancellationToken).ConfigureAwait(false);
        return (sidecar.History ?? [])
            .OrderByDescending(v => v.Number)
            .Select(v => new LabelVersionInfo(v.Number, v.SavedAt, v.SavedBy, v.Note, v.Size))
            .ToList();
    }

    public async Task<LabelLayout> LoadVersionAsync(string path, int number, CancellationToken cancellationToken = default)
    {
        var sidecar = await EditorMetadataSidecar.LoadAsync(path, cancellationToken).ConfigureAwait(false);
        var version = (sidecar.History ?? []).FirstOrDefault(v => v.Number == number)
            ?? throw new LabelDocumentException($"Version {number} of '{Path.GetFileName(path)}' was not found.");
        using var stream = new MemoryStream(ContentOf(version));
        return await format.ReadAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    private async Task<LabelLayout> ReadLayoutAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = LeditFieldCatalogSource.OpenRead(path);
            return await format.ReadAsync(stream, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new LabelDocumentException($"Could not open '{path}': {ex.Message}", ex);
        }
    }
}
