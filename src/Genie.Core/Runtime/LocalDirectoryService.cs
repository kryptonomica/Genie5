namespace Genie.Core.Runtime;

public sealed class LocalDirectoryService
{
    private readonly string _appName;
    private readonly string _baseDirectory;

    public LocalDirectoryService(string appName, string baseDirectory)
    {
        _appName = appName;
        _baseDirectory = baseDirectory;
        Current = AppPaths.Discover(appName, baseDirectory);
    }

    public AppPaths Current { get; private set; }

    public void CheckUserDirectory()
    {
        Current = AppPaths.Discover(_appName, _baseDirectory);
    }

    public void SetUserDataDirectory()
    {
        Current = AppPaths.Discover(_appName, _baseDirectory);
    }

    public string ValidateDirectory(string configuredPath) => Current.ValidateDirectory(configuredPath);
}
