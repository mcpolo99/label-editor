using LabelEditor.Application.Abstractions;
using LabelEditor.Domain.Layout;

namespace LabelEditor.Application.Documents;

/// <summary>Opening, creating and saving label documents.</summary>
public sealed class DocumentService(ILabelDocumentStore store, IStorageLocations locations)
{
    public static EditorSession CreateNew(int width = 100_000, int height = 60_000) => new(LabelLayout.CreateNew(width, height));

    public async Task<EditorSession> OpenAsync(string path, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        var layout = await store.LoadAsync(fullPath, cancellationToken).ConfigureAwait(false);
        return new EditorSession(layout, new DocumentLocation(fullPath, IsReadOnly(fullPath)));
    }

    /// <summary>
    /// Opens a file as a new, untitled document (used for templates and read-only machine labels,
    /// which must be saved somewhere else).
    /// </summary>
    public async Task<EditorSession> OpenAsCopyAsync(string path, CancellationToken cancellationToken = default)
    {
        var layout = await store.LoadAsync(Path.GetFullPath(path), cancellationToken).ConfigureAwait(false);
        return new EditorSession(layout);
    }

    /// <summary>True when the session has a location that is not write protected (it may still need a replace confirmation).</summary>
    public static bool CanSaveInPlace(EditorSession session) => session.Location is { IsReadOnly: false };

    /// <summary>Saves to the session's current location.</summary>
    /// <exception cref="InvalidOperationException">The document has no location or it is write protected; use <see cref="SaveAsAsync"/>.</exception>
    public Task SaveAsync(EditorSession session, CancellationToken cancellationToken = default)
    {
        if (!CanSaveInPlace(session))
        {
            throw new InvalidOperationException("This document must be saved under a new name.");
        }

        return SaveToAsync(session, session.Location!.Path, cancellationToken);
    }

    public Task SaveAsAsync(EditorSession session, string path, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        if (locations.IsWriteProtected(fullPath))
        {
            throw new LabelDocumentException($"'{fullPath}' is in a write-protected folder. Choose another location.");
        }

        return SaveToAsync(session, fullPath, cancellationToken);
    }

    public bool IsReadOnly(string path) => locations.IsWriteProtected(path);

    private async Task SaveToAsync(EditorSession session, string path, CancellationToken cancellationToken)
    {
        // Snapshot first: the layout is immutable, so the user may keep editing while the write runs.
        var snapshot = session.Layout;

        // No ConfigureAwait(false): the session raises UI notifications and must be touched on the caller's context.
        await store.SaveAsync(snapshot, path, cancellationToken);
        session.MarkSaved(new DocumentLocation(path, IsReadOnly: false), snapshot);
    }
}
