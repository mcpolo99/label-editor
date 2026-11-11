using LabelEditor.Application.Abstractions;
using LabelEditor.Domain.Layout;
using LabelEditor.Infrastructure.Ledit;

namespace LabelEditor.Infrastructure.Storage;

/// <summary>Label files on disk, read and written asynchronously; saves are atomic.</summary>
public sealed class FileLabelDocumentStore(ILabelLayoutFormat format) : ILabelDocumentStore
{
    public async Task<LabelLayout> LoadAsync(string path, CancellationToken cancellationToken = default)
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

    public async Task SaveAsync(LabelLayout layout, string path, CancellationToken cancellationToken = default)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(path)) ?? throw new LabelDocumentException($"Invalid path '{path}'.");
        Directory.CreateDirectory(folder);

        // Write next to the target, then swap: a crash or full disk never leaves a half-written label.
        var temp = Path.Combine(folder, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 16 * 1024, FileOptions.Asynchronous))
            {
                await format.WriteAsync(layout, stream, cancellationToken).ConfigureAwait(false);
            }

            File.Move(temp, path, overwrite: true);
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
}
