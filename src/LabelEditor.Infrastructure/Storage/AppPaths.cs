namespace LabelEditor.Infrastructure.Storage;

/// <summary>
/// Per-user paths. In portable mode (a file named <c>portable</c> next to the executable) everything is
/// kept beside the executable, so the editor can run from a USB stick or a network share without
/// touching the user profile.
/// </summary>
public sealed record AppPaths(string ApplicationFolder, string UserDataFolder, bool IsPortable)
{
    public const string ProductFolderName = "LabelEditor";
    public const string PortableMarkerFileName = "portable";

    public string SettingsFile => Path.Combine(UserDataFolder, "settings.json");

    public string DefaultWorkingFolder => IsPortable
        ? Path.Combine(UserDataFolder, "Labels")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), ProductFolderName, "Labels");

    public string PackagedDefaultsRoot => Path.Combine(ApplicationFolder, "Defaults", "LEdit");

    public static AppPaths Detect(string? applicationFolder = null)
    {
        var appFolder = applicationFolder ?? AppContext.BaseDirectory;
        var portable = File.Exists(Path.Combine(appFolder, PortableMarkerFileName));
        var userData = portable
            ? Path.Combine(appFolder, "UserData")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ProductFolderName);
        return new AppPaths(appFolder, userData, portable);
    }
}
