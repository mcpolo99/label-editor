namespace LabelEditor.Application.Settings;

/// <summary>User preferences, persisted between runs.</summary>
public sealed record EditorSettings
{
    /// <summary>LEdit language name; null = pick from the OS culture.</summary>
    public string? Language { get; init; }

    public bool SnapToGrid { get; init; } = true;

    public bool ShowGrid { get; init; } = true;

    public double Zoom { get; init; } = 2.0;

    /// <summary>Override for the installed LEdit folder; null = auto-detect.</summary>
    public string? LeditInstallationPath { get; init; }

    /// <summary>Override for the writable working folder; null = default.</summary>
    public string? WorkingFolder { get; init; }

    public IReadOnlyList<string> RecentFiles { get; init; } = [];

    public const int MaxRecentFiles = 10;

    public EditorSettings WithRecentFile(string path) => this with
    {
        RecentFiles = [path, .. RecentFiles.Where(p => !string.Equals(p, path, StringComparison.OrdinalIgnoreCase)).Take(MaxRecentFiles - 1)],
    };
}
