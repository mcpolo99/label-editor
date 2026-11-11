using System.Text;
using LabelEditor.Application.Abstractions;
using LabelEditor.Infrastructure.Ledit;
using Microsoft.Extensions.Logging;

namespace LabelEditor.Infrastructure.Storage;

/// <summary>Lists label layouts in the configured folders.</summary>
public sealed class FileSystemLabelLibrary(IStorageLocations locations, ILogger<FileSystemLabelLibrary> logger) : ILabelLibrary
{
    private const int SniffLength = 512;

    public async Task<IReadOnlyList<LabelLibraryItem>> GetItemsAsync(CancellationToken cancellationToken = default)
    {
        var items = new List<LabelLibraryItem>();
        foreach (var folder in locations.LabelFolders)
        {
            if (!Directory.Exists(folder.Path))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(folder.Path, "*.xml"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (await IsLabelLayoutAsync(file, cancellationToken).ConfigureAwait(false))
                {
                    items.Add(new LabelLibraryItem(
                        Path.GetFileNameWithoutExtension(file),
                        file,
                        folder.Kind,
                        folder.IsReadOnly || locations.IsWriteProtected(file),
                        File.GetLastWriteTimeUtc(file)));
                }
            }
        }

        return items.OrderBy(i => i.Source).ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Peeks at the file head: the label folder also holds other XML (e.g. LabelLayoutsSources.xml).</summary>
    private async Task<bool> IsLabelLayoutAsync(string file, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = LeditFieldCatalogSource.OpenRead(file);
            var buffer = new byte[SniffLength];
            var read = await stream.ReadAtLeastAsync(buffer, SniffLength, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);
            return Encoding.Latin1.GetString(buffer, 0, read).Contains("<LabelLayout", StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Skipping unreadable file {File}", file);
            return false;
        }
    }
}
