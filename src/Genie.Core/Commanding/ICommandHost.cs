namespace Genie.Core.Commanding;

public interface ICommandHost
{
    void Echo(string text);
    void EchoTo(string text, string? window, string? color);
    /// <summary>
    /// Send a command line to the game socket.
    /// </summary>
    /// <param name="text">The literal bytes to put on the wire — what the
    /// server will parse. For DR's UI-link clicks this is the cmd attribute
    /// with item-exist-IDs (e.g. <c>get #49489411 in #49489410</c>).</param>
    /// <param name="userInput">True when this came from typed input / link
    /// click — produces a local echo. False for script/alias-emitted sends
    /// (those have their own echo paths).</param>
    /// <param name="origin">Optional tag for telemetry / mapper.</param>
    /// <param name="echoOverride">Optional friendly string to echo INSTEAD of
    /// <paramref name="text"/> when <paramref name="userInput"/> is true.
    /// Used by the UI link click path so the user sees
    /// "get a tapered cutlass" instead of "get #49489411 in #49489410" in
    /// the Game window — the server still receives the IDs.</param>
    void SendToGame(string text, bool userInput = false, string origin = "", string? echoOverride = null);
    void RunScript(string text);

    /// <summary>
    /// Stop a running script. <paramref name="name"/> null/empty stops the
    /// most recently started script; a name stops that specific script.
    /// Used by <c>#stop</c> from the command bar.
    /// </summary>
    void StopScript(string? name);

    /// <summary>Stop every running script. Used by <c>#stopall</c>.</summary>
    void StopAllScripts();

    /// <summary>
    /// Names of currently running scripts. Used by <c>#scripts</c> to list
    /// them at the command bar.
    /// </summary>
    IReadOnlyList<string> RunningScripts();

    /// <summary>
    /// Set (or replace) a session-global <c>$variable</c>. Used by
    /// <c>#tvar</c> from the command bar — the value lives in the same
    /// dictionary scripts read for <c>$name</c> expansion.
    /// </summary>
    void SetGlobalVariable(string name, string value);

    /// <summary>Remove a session-global variable.</summary>
    void RemoveGlobalVariable(string name);

    /// <summary>
    /// Expand <c>$name</c> references to their current global value (from
    /// the script engine's Globals — populated by <c>#var</c>/<c>#tvar</c>
    /// and the live-game-state mirror). Matches Genie 4's
    /// <c>ParseGlobalVars</c>: called at the command-bar entry point so a
    /// user typing <c>#echo $health</c> sees the substituted number.
    /// Unknown vars are left as the literal <c>$name</c> text (Genie 4
    /// parity); use an empty fallback for read-or-empty intent.
    /// </summary>
    string ExpandVariables(string text);

    /// <summary>
    /// Open the named script file (<c>{ScriptsDir}/{name}.cmd</c> or
    /// <c>.inc</c>) in the user's external editor — either the path
    /// configured via Display Settings or the OS default `.cmd` handler.
    /// Wired to <c>#edit &lt;name&gt;</c> from the command bar plus the
    /// pencil button on the Script Bar.
    /// <para>
    /// Implemented in the App layer because it needs <c>Process.Start</c>
    /// plus cross-platform launch semantics that don't belong in
    /// <see cref="Genie.Core"/>. <see cref="GenieCore"/>'s implementation
    /// is a no-op (Echo a "no editor host" message) — the App overrides
    /// it via the same <c>ICommandHost</c> instance.
    /// </para>
    /// </summary>
    void EditScript(string name);

    /// <summary>
    /// Run a <c>#layout</c> command — the raw argument string after
    /// <c>#layout </c> (e.g. <c>save global My Layout</c>, <c>load Base</c>,
    /// <c>list</c>). Layout storage + dock manipulation live in the App layer,
    /// so <see cref="Genie.Core"/> forwards the args to a host handler; the
    /// Console build with no handler echoes a diagnostic.
    /// </summary>
    void LayoutCommand(string args);

    /// <summary>
    /// Run a <c>#plugin</c> command — the raw argument string after
    /// <c>#plugin </c> (e.g. <c>list</c>, <c>enable Experience</c>,
    /// <c>unload genie.experience</c>, <c>load Plugin_EXPTrackerV5</c>). Plugin
    /// management (loader + folder) is orchestrated by the App layer, so
    /// <see cref="Genie.Core"/> forwards the args to a host handler; the Console
    /// build with no handler echoes a diagnostic.
    /// </summary>
    void PluginCommand(string args);
}
