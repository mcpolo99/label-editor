namespace LabelEditor.Application.Settings;

/// <summary>Colour theme of the editor window.</summary>
public enum AppTheme
{
    /// <summary>Follow the Windows light / dark app mode.</summary>
    System,
    Light,
    Dark,
}

/// <summary>User preferences, persisted between runs.</summary>
public sealed record EditorSettings
{
    public AppTheme Theme { get; init; } = AppTheme.System;

    /// <summary>In the dark theme, show the label paper light grey instead of bright white (easier on the eyes).</summary>
    public bool DimLabelInDarkTheme { get; init; } = true;

    /// <summary>LEdit language name; null = pick from the OS culture.</summary>
    public string? Language { get; init; }

    public bool SnapToGrid { get; init; } = true;

    public bool ShowGrid { get; init; } = true;

    /// <summary>While dragging, line up edges and centres with other entries and the label (guide lines).</summary>
    public bool SnapToEntries { get; init; } = true;

    public bool ShowRulers { get; init; } = true;

    public double Zoom { get; init; } = 2.0;

    /// <summary>
    /// Before saving, warn about texts that are too wide for their field at their font size. Off by default:
    /// the canvas always outlines such texts, and many existing labels have some.
    /// </summary>
    public bool WarnTextOverflowOnSave { get; init; }

    /// <summary>Override for the installed LEdit folder; null = auto-detect.</summary>
    public string? LeditInstallationPath { get; init; }

    /// <summary>
    /// Allow saving into the installed LEdit's label folder (production data). Off by default; labels from there
    /// can always be opened and edited, and saved elsewhere.
    /// </summary>
    public bool AllowWritingMachineLabels { get; init; }

    /// <summary>Override for the writable working folder; null = default.</summary>
    public string? WorkingFolder { get; init; }

    public IReadOnlyList<string> RecentFiles { get; init; } = [];

    public const int MaxRecentFiles = 10;

    public EditorSettings WithRecentFile(string path) => this with
    {
        RecentFiles = [path, .. RecentFiles.Where(p => !string.Equals(p, path, StringComparison.OrdinalIgnoreCase)).Take(MaxRecentFiles - 1)],
    };
}
