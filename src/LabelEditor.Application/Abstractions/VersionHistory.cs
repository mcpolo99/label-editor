using LabelEditor.Domain.Layout;

namespace LabelEditor.Application.Abstractions;

/// <summary>One saved version of a label.</summary>
/// <param name="Number">1-based, increasing with every save that changed the label.</param>
/// <param name="SavedAt">When it was saved (for the captured original: the file's last write time).</param>
/// <param name="SavedBy">Windows user who saved it.</param>
/// <param name="Note">E.g. "Original" for the content found on disk before the editor first replaced it.</param>
/// <param name="SizeBytes">Size of the LEdit XML.</param>
public sealed record LabelVersionInfo(int Number, DateTimeOffset SavedAt, string SavedBy, string? Note, int SizeBytes);

/// <summary>
/// Version history of label files. The document store records a version on every save that changes the
/// label, so labels keep one clean name instead of _v1/_v2 copies.
/// </summary>
public interface ILabelVersionHistory
{
    /// <summary>Versions of the label at <paramref name="path"/>, newest first. Empty when there is no history.</summary>
    Task<IReadOnlyList<LabelVersionInfo>> GetVersionsAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>The label as it was saved in version <paramref name="number"/>.</summary>
    Task<LabelLayout> LoadVersionAsync(string path, int number, CancellationToken cancellationToken = default);
}
