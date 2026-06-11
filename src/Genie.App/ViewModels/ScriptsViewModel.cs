using System.Collections.ObjectModel;
using System.IO;
using System.Reactive;
using System.Reactive.Linq;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia;
using Genie.Core;
using Genie.Core.Scripting;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Genie.App.ViewModels;

/// <summary>
/// Backs the Scripts dock tool: a live list of running scripts, per-script
/// stop / pause buttons, a Start... button that opens a file picker, and a
/// scrollable output log of every <c>[script]</c>-prefixed echo the engine
/// emits.
///
/// Lifecycle: subscribes to <c>ScriptStarted</c> and <c>ScriptFinished</c>
/// events on <see cref="ScriptEngine"/> to keep the running-list in sync,
/// and to <see cref="GenieCore.ScriptOutputLine"/> for the log. All UI-bound
/// mutations marshal onto the UI thread via <see cref="RxApp.MainThreadScheduler"/>.
/// </summary>
public class ScriptsViewModel : ReactiveObject
{
    private const int MaxOutputLines = 2000;

    private GenieCore? _core;

    /// <summary>Live list of currently-running scripts. Each row exposes
    /// the script name plus a <c>StopCommand</c> bound to a per-row button.</summary>
    public ObservableCollection<RunningScriptRow> RunningScripts { get; } = [];

    /// <summary>Rolling buffer of script-originated echo output. Truncates
    /// to <see cref="MaxOutputLines"/> so a long-running script with verbose
    /// debug output can't memory-leak the panel.</summary>
    public ObservableCollection<TextLine> Output { get; } = [];

    [Reactive] public string StatusText { get; private set; } = "Not connected.";

    public ReactiveCommand<Unit, Unit> StartScriptCommand { get; }
    public ReactiveCommand<Unit, Unit> StopAllCommand     { get; }
    public ReactiveCommand<Unit, Unit> ClearOutputCommand { get; }

    public ScriptsViewModel()
    {
        StartScriptCommand = ReactiveCommand.CreateFromTask(StartScriptAsync);
        StopAllCommand     = ReactiveCommand.Create(StopAll);
        ClearOutputCommand = ReactiveCommand.Create(() => Output.Clear());
    }

    /// <summary>
    /// Bind this VM to a live <see cref="GenieCore"/>. Called by
    /// <see cref="MainWindowViewModel"/> once per connect — replays the
    /// existing running-script list (in case scripts auto-started during
    /// startup) and hooks ScriptStarted / ScriptFinished / ScriptOutputLine
    /// for ongoing updates.
    /// </summary>
    public void Attach(GenieCore core)
    {
        _core = core;
        RunningScripts.Clear();
        Output.Clear();
        StatusText = "Connected.";

        // Seed any scripts already running (auto-connect scripts, etc.) —
        // both .cmd and .js.
        foreach (var name in core.Scripts.RunningScriptNames())
            RunningScripts.Add(MakeRow(name));

        // Add/remove rows reactively. Both event paths marshal to the UI
        // thread since the engine ticks from connection / parser threads.
        Observable.FromEvent<Action<string>, string>(
                h => core.Scripts.ScriptStarted += h,
                h => core.Scripts.ScriptStarted -= h)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(name =>
            {
                // Reload semantics — a script that re-starts gets its
                // previous row replaced rather than duplicated.
                for (int i = RunningScripts.Count - 1; i >= 0; i--)
                    if (string.Equals(RunningScripts[i].Name, name, StringComparison.OrdinalIgnoreCase))
                        RunningScripts.RemoveAt(i);
                RunningScripts.Add(MakeRow(name));
            });

        Observable.FromEvent<Action<string>, string>(
                h => core.Scripts.ScriptFinished += h,
                h => core.Scripts.ScriptFinished -= h)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(name =>
            {
                for (int i = RunningScripts.Count - 1; i >= 0; i--)
                    if (string.Equals(RunningScripts[i].Name, name, StringComparison.OrdinalIgnoreCase))
                        RunningScripts.RemoveAt(i);
            });

        Observable.FromEvent<Action<string>, string>(
                h => core.ScriptOutputLine += h,
                h => core.ScriptOutputLine -= h)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(msg =>
            {
                Output.Add(new TextLine(msg, StreamColor.System));
                while (Output.Count > MaxOutputLines)
                    Output.RemoveAt(0);
            });
    }

    private void StopAll()
    {
        if (_core is null) return;
        _core.Commands.ProcessInput("#stopall");
    }

    /// <summary>
    /// Stop a single running script by name. Used by the per-row Stop
    /// button via <see cref="StopCmd"/>.
    /// </summary>
    private void StopOne(string name)
    {
        if (_core is null || string.IsNullOrEmpty(name)) return;
        // Route through the command pipeline so behavior matches the user
        // typing `#stop name` at the bar (echoes, finish event, etc.).
        _core.Commands.ProcessInput($"#stop {name}");
    }

    /// <summary>
    /// Pick a script file via the platform file picker, then invoke it
    /// through the normal command pipeline (<c>.scriptname</c>). Going
    /// through the pipeline means aliases / class gating / dbg routing all
    /// work the same way as command-bar invocation.
    /// </summary>
    private async Task StartScriptAsync()
    {
        if (_core is null) return;

        if (Application.Current?.ApplicationLifetime is not
                IClassicDesktopStyleApplicationLifetime desktop) return;
        if (desktop.MainWindow?.StorageProvider is not { } sp) return;

        IStorageFolder? startLocation = null;
        var dir = _core.Scripts.ScriptsDir;
        if (Directory.Exists(dir))
        {
            try   { startLocation = await sp.TryGetFolderFromPathAsync(dir); }
            catch { /* picker uses its default location */ }
        }

        var picked = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title                  = "Run script",
            AllowMultiple          = false,
            SuggestedStartLocation = startLocation,
            FileTypeFilter         = new[]
            {
                new FilePickerFileType("Genie scripts") { Patterns = ["*.cmd", "*.inc", "*.js"] },
                new FilePickerFileType("All files")     { Patterns = ["*"]              },
            },
        });

        var file = picked?.FirstOrDefault();
        if (file is null) return;

        var path = file.Path.LocalPath;
        if (string.IsNullOrWhiteSpace(path)) return;

        // Strip extension — the script char prefix (`.`) plus bare name is
        // the canonical invocation form; the engine resolves to .cmd / .inc.
        var name = Path.GetFileNameWithoutExtension(path);
        _core.Commands.ProcessInput($".{name}");
    }

    /// <summary>
    /// Build a <see cref="RunningScriptRow"/> with its per-row Stop command
    /// pre-baked. Created via helper rather than a sub-class so the implicit
    /// conversion / record-init wiring stays straightforward.
    /// </summary>
    private RunningScriptRow MakeRow(string name)
        => new(name,
               _core?.Scripts.IsJavaScript(name) ?? false,
               ReactiveCommand.Create(() => StopOne(name)));
}

/// <summary>
/// One row in the running-scripts grid. Records carry their own Stop
/// command so the AXAML <c>Button.Command</c> binding doesn't need a
/// converter / collection-relative lookup. Top-level (not nested in
/// <see cref="ScriptsViewModel"/>) so Avalonia AXAML's compiled
/// <c>x:DataType</c> resolution can find it — nested types via
/// <c>Owner+Inner</c> syntax don't compile cleanly there.
/// </summary>
public sealed record RunningScriptRow(string Name, bool IsJavaScript, ReactiveCommand<Unit, Unit> StopCommand);
