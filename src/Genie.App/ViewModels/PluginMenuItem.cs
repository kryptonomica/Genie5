using System.Reactive;
using Genie.Plugins;
using ReactiveUI;

namespace Genie.App.ViewModels;

/// <summary>
/// Per-plugin entry for the Plugins menu: shows the plugin's name/version and a
/// checkbox that flips <see cref="IGeniePlugin.Enabled"/>. Wraps the plugin
/// (which isn't reactive) so the menu checkmark refreshes on toggle.
/// </summary>
public sealed class PluginMenuItem : ReactiveObject
{
    private readonly IGeniePlugin _plugin;

    public PluginMenuItem(IGeniePlugin plugin, Action onUnload)
    {
        _plugin       = plugin;
        ToggleCommand = ReactiveCommand.Create(() => { Enabled = !Enabled; });
        UnloadCommand = ReactiveCommand.Create(onUnload);
    }

    public string Display => $"{_plugin.Name}  v{_plugin.Version}";

    public bool Enabled
    {
        get => _plugin.Enabled;
        set
        {
            if (_plugin.Enabled == value) return;
            _plugin.Enabled = value;
            this.RaisePropertyChanged();
        }
    }

    public ReactiveCommand<Unit, Unit> ToggleCommand { get; }

    /// <summary>Fully unload the plugin (remove + unload its load context).
    /// Distinct from <see cref="Enabled"/>, which only disables it.</summary>
    public ReactiveCommand<Unit, Unit> UnloadCommand { get; }
}
