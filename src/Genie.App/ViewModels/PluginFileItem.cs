using System.Reactive;
using ReactiveUI;

namespace Genie.App.ViewModels;

/// <summary>
/// An unloaded plugin DLL sitting in the Plugins folder, shown in the
/// Plugins → Load submenu so each can be loaded individually (the counterpart
/// to the per-plugin Unload).
/// </summary>
public sealed class PluginFileItem : ReactiveObject
{
    public PluginFileItem(string fileName, Action onLoad)
    {
        Display     = fileName;
        LoadCommand = ReactiveCommand.Create(onLoad);
    }

    /// <summary>The DLL file name (e.g. "Plugin_EXPTrackerV5.dll").</summary>
    public string Display { get; }

    public ReactiveCommand<Unit, Unit> LoadCommand { get; }
}
