using System.Collections.ObjectModel;
using System.Reactive;
using Avalonia.Threading;
using Genie.Core;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Genie.App.ViewModels;

/// <summary>
/// Thin status strip showing which <c>.cmd</c> scripts are currently running.
/// Mirrors Genie 4's "Script Bar" muscle memory — you can see at a glance
/// what's executing and stop individual scripts without typing
/// <c>#stop &lt;name&gt;</c>.
///
/// <para>
/// The bar is auto-hidden when no scripts are running (see <see cref="HasScripts"/>),
/// so it occupies zero vertical space during ordinary play. Genie 4's bar
/// is always-visible; we go invisible-when-empty to keep the alpha UI quiet.
/// </para>
/// </summary>
public sealed class ScriptBarViewModel : ReactiveObject
{
    private GenieCore? _core;

    /// <summary>
    /// Scripts currently in the engine's instance list (both <c>.cmd</c> and
    /// <c>.js</c>). Updated on <see cref="ScriptEngine.ScriptStarted"/> /
    /// <see cref="ScriptEngine.ScriptFinished"/> events, always marshalled to the
    /// UI thread before mutating the ObservableCollection (Avalonia's ItemsControl
    /// requires UI-thread modifications). Each item carries its language so the
    /// bar can tag <c>.js</c> rows.
    /// </summary>
    public ObservableCollection<ScriptBarItem> RunningScripts { get; } = new();

    /// <summary>
    /// True when at least one script is in <see cref="RunningScripts"/>.
    /// Bound to the strip's <c>IsVisible</c> in XAML so the bar collapses
    /// to zero height between sessions.
    /// </summary>
    [Reactive] public bool HasScripts { get; private set; }

    /// <summary>
    /// Sends <c>#stop &lt;name&gt;</c> to the script engine. Parameterised
    /// so the XAML <c>Button.Command</c> can pass the per-item name via
    /// <c>CommandParameter</c>.
    /// </summary>
    public ReactiveCommand<string, Unit> StopScriptCommand { get; }

    /// <summary>
    /// Opens the named script in the user-configured external editor (or
    /// the OS default if none configured). Wired in Task #188 (Edit Script).
    /// Parameterised same as <see cref="StopScriptCommand"/>.
    /// </summary>
    public ReactiveCommand<string, Unit> EditScriptCommand { get; }

    public ScriptBarViewModel()
    {
        StopScriptCommand = ReactiveCommand.Create<string, Unit>(name =>
        {
            if (!string.IsNullOrWhiteSpace(name))
                _core?.Scripts.Stop(name);
            return Unit.Default;
        });

        EditScriptCommand = ReactiveCommand.Create<string, Unit>(name =>
        {
            if (string.IsNullOrWhiteSpace(name) || _core is null) return Unit.Default;
            EditScript?.Invoke(name);
            return Unit.Default;
        });
    }

    /// <summary>
    /// Raised when the user picks Edit on a script. The host
    /// (<see cref="MainWindowViewModel"/>) handles the actual editor
    /// launch since it owns the <c>DisplaySettings.EditorPath</c>
    /// setting plus the cross-platform <c>Process.Start</c> details.
    /// </summary>
    public event Action<string>? EditScript;

    public void Attach(GenieCore core)
    {
        _core = core;

        core.Scripts.ScriptStarted += name =>
        {
            // Resolve the language now, on the engine thread — an ultra-short
            // .js script could finish before the marshalled add runs.
            var isJs = core.Scripts.IsJavaScript(name);
            Dispatcher.UIThread.Post(() =>
            {
                // Reload semantics: replace an existing same-named row.
                for (int i = RunningScripts.Count - 1; i >= 0; i--)
                    if (RunningScripts[i].Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                        RunningScripts.RemoveAt(i);
                RunningScripts.Add(new ScriptBarItem(name, isJs));
                HasScripts = RunningScripts.Count > 0;
            });
        };

        core.Scripts.ScriptFinished += name =>
            Dispatcher.UIThread.Post(() =>
            {
                for (int i = RunningScripts.Count - 1; i >= 0; i--)
                    if (RunningScripts[i].Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                        RunningScripts.RemoveAt(i);
                HasScripts = RunningScripts.Count > 0;
            });
    }
}

/// <summary>One running-script chip in the script bar. Carries the language so
/// the bar can show a "js" tag; the Stop/Edit commands key off <see cref="Name"/>.</summary>
public sealed record ScriptBarItem(string Name, bool IsJavaScript);
