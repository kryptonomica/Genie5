using System.Runtime.InteropServices;

namespace Genie.Core.Runtime;

public sealed class AppPaths
{
    public AppPaths(string basePath, bool isLocal)
    {
        BasePath = Path.GetFullPath(basePath);
        IsLocal = isLocal;
    }

    public string BasePath { get; }
    public bool IsLocal { get; }

    /// <summary>
    /// Sentinel file that, when present next to the executable, puts Genie in
    /// portable mode: all data folders (Config, Scripts, Maps, Plugins, Logs,
    /// Profiles, Layouts, …) live beside the exe instead of in the per-user
    /// data directory. The portable .zip ships with this file; the installer
    /// does not. Contents are ignored — only its presence matters.
    /// </summary>
    public const string PortableMarkerFileName = "genie5.portable";

    public static AppPaths Discover(string appName, string baseDirectory)
    {
        // Portable mode is signalled by an explicit marker file beside the
        // exe. The legacy "a Config folder already exists here" check is kept
        // as a secondary trigger so existing hand-made local installs keep
        // working without adding the marker.
        var markerPath = Path.Combine(baseDirectory, PortableMarkerFileName);
        var legacyLocalConfig = Path.Combine(baseDirectory, "Config");
        var wantsPortable = File.Exists(markerPath) || Directory.Exists(legacyLocalConfig);

        // Guard: only honor portable mode if we can actually write here. A
        // copy dropped in Program Files (or any read-only location) falls back
        // to the per-user data dir rather than failing to save settings.
        if (wantsPortable && IsDirectoryWritable(baseDirectory))
        {
            return new AppPaths(baseDirectory, isLocal: true);
        }

        // Windows: %APPDATA%\AppName
        // macOS:   ~/Library/Application Support/AppName
        // Linux:   $XDG_DATA_HOME/AppName (fallback: ~/.local/share/AppName)
        string userDir;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            userDir = Path.Combine(roaming, appName);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            userDir = Path.Combine(home, "Library", "Application Support", appName);
        }
        else
        {
            // Linux / other Unix — respect XDG_DATA_HOME
            var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            if (string.IsNullOrEmpty(xdg))
                xdg = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
            userDir = Path.Combine(xdg, appName);
        }

        Directory.CreateDirectory(userDir);
        return new AppPaths(userDir, isLocal: false);
    }

    /// <summary>
    /// True if we can create/delete a file in <paramref name="dir"/>. Used to
    /// decide whether portable mode is actually viable at this location.
    /// </summary>
    private static bool IsDirectoryWritable(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            var probe = Path.Combine(dir, ".genie5-write-probe");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public string ResolvePath(string configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
            return BasePath;
        if (Path.IsPathRooted(configuredPath))
            return Path.GetFullPath(configuredPath);
        return Path.GetFullPath(Path.Combine(BasePath, configuredPath));
    }

    public string ValidateDirectory(string configuredPath)
    {
        var fullPath = ResolvePath(configuredPath);
        Directory.CreateDirectory(fullPath);
        return fullPath;
    }
}
