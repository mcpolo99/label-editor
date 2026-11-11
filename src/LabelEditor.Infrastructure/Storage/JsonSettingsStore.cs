using System.Text.Json;
using LabelEditor.Application.Abstractions;
using LabelEditor.Application.Settings;
using Microsoft.Extensions.Logging;

namespace LabelEditor.Infrastructure.Storage;

/// <summary>Settings as JSON in the user data folder. A missing or corrupt file yields defaults.</summary>
public sealed class JsonSettingsStore(string filePath, ILogger<JsonSettingsStore> logger) : ISettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public async Task<EditorSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            return new EditorSettings();
        }

        try
        {
            await using var stream = File.OpenRead(filePath);
            return await JsonSerializer.DeserializeAsync<EditorSettings>(stream, Options, cancellationToken).ConfigureAwait(false) ?? new EditorSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Ignoring unreadable settings file {File}", filePath);
            return new EditorSettings();
        }
    }

    public async Task SaveAsync(EditorSettings settings, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        var temp = filePath + ".tmp";
        await using (var stream = File.Create(temp))
        {
            await JsonSerializer.SerializeAsync(stream, settings, Options, cancellationToken).ConfigureAwait(false);
        }

        File.Move(temp, filePath, overwrite: true);
    }
}
