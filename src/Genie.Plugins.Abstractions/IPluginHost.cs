namespace Genie.Plugins;

/// <summary>
/// The capabilities the host (Genie 5) exposes to a plugin. Passed to
/// <see cref="IGeniePlugin.Initialize"/>. This is the entire surface a plugin
/// may touch — intentionally small and UI-free so the same contract works for
/// in-process plugins and DLL-loaded plugins, and so a plugin can never reach
/// UI internals or the policy-gated paths (no headless, no AI→ProcessInput,
/// no auto-reconnect) directly.
/// </summary>
public interface IPluginHost
{
    /// <summary>Host (Genie 5) app version string, e.g. "5.0.0-alpha.1".</summary>
    string HostVersion { get; }

    /// <summary>
    /// Plugin-API contract version (Genie 4 <c>IHost.InterfaceVersion</c>
    /// parity). A monotonic integer bumped only on a breaking change to
    /// <see cref="IGeniePlugin"/> / <see cref="IPluginHost"/>, so a plugin can
    /// adapt at runtime instead of failing to load.
    /// </summary>
    int InterfaceVersion { get; }

    // ── Output ───────────────────────────────────────────────────────────────

    /// <summary>Write a line to the main game window.</summary>
    void Echo(string text);

    /// <summary>
    /// Append a line to a named plugin window. The App surfaces unknown window
    /// names as dock panels (the Genie 4 <c>#echo &gt;Name</c> model). This is
    /// the UI seam that keeps plugins free of any Avalonia/app dependency.
    /// </summary>
    void EchoToWindow(string window, string text);

    /// <summary>
    /// Replace a named plugin window's entire contents in one call — for
    /// snapshot-style windows (e.g. the Experience tracker re-renders its whole
    /// list each update) rather than append-style logs. <paramref name="content"/>
    /// may contain newlines.
    /// </summary>
    void SetWindow(string window, string content);

    /// <summary>
    /// Send a command to the game, exactly as if the user typed it. Subject to
    /// the same policy gates as user input. Plugins must not use this to build
    /// agentive automation that runs while the user is away.
    /// </summary>
    void SendCommand(string command);

    // ── Variables (shared with the script-engine globals) ────────────────────

    /// <summary>Live snapshot of session globals (the same surface scripts read
    /// for <c>$name</c>).</summary>
    IReadOnlyDictionary<string, string> Variables { get; }

    string? GetVariable(string name);
    void    SetVariable(string name, string value);

    // ── State ─────────────────────────────────────────────────────────────────

    /// <summary>Read-only view of live game state.</summary>
    IGameStateView State { get; }

    // ── Diagnostics ────────────────────────────────────────────────────────────

    /// <summary>Write to the host's diagnostic log (not the game window).</summary>
    void Log(string message);
}
