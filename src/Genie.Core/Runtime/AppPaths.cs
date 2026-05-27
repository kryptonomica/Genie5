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

    public static AppPaths Discover(string appName, string baseDirectory)
    {
        var localConfig = Path.Combine(baseDirectory, "Config");
        if (Directory.Exists(localConfig))
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
