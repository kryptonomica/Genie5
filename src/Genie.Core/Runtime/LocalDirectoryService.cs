namespace Genie.Core.Runtime;

public sealed class LocalDirectoryService
{
    private readonly string _appName;
    private readonly string _baseDirectory;

    public LocalDirectoryService(string appName, string baseDirectory)
    {
        _appName = appName;
        _baseDirectory = baseDirectory;
        Current = Shared = AppPaths.Discover(appName, baseDirectory);
    }

    /// <summary>
    /// The active data root: the discovered (AppData / portable) location, or a
    /// per-profile override set via <see cref="UseExplicitRoot"/>. Most data
    /// (Config, Scripts, Logs, Profiles) resolves against this.
    /// </summary>
    public AppPaths Current { get; private set; }

    /// <summary>
    /// The always-shared root: the discovered (AppData / portable) location,
    /// never affected by a per-profile override. Community/shared resources
    /// (e.g. Maps) resolve against this so every profile reads one copy. When
    /// no override is active this equals <see cref="Current"/>.
    /// </summary>
    public AppPaths Shared { get; private set; }

    public void CheckUserDirectory()
    {
        Current = Shared = AppPaths.Discover(_appName, _baseDirectory);
    }

    public void SetUserDataDirectory()
    {
        Current = Shared = AppPaths.Discover(_appName, _baseDirectory);
    }

    /// <summary>
    /// Point the data root at an explicit folder, overriding the discovered
    /// (AppData / portable) location. Used for a per-profile data directory:
    /// when a profile specifies its own folder, all data resolves under it.
    /// The folder is created if needed. Empty/whitespace is ignored (keeps the
    /// discovered root).
    /// </summary>
    public void UseExplicitRoot(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath)) return;
        var resolved = Path.GetFullPath(fullPath);
        Directory.CreateDirectory(resolved);
        Current = new AppPaths(resolved, isLocal: true);
    }

    public string ValidateDirectory(string configuredPath) => Current.ValidateDirectory(configuredPath);
}
