namespace Genie.Plugins;

/// <summary>
/// A Genie 5 plugin. Implemented by both in-process plugins (registered at
/// startup) and DLL-loaded plugins (discovered from the Plugins folder) — the
/// load path differs, the contract does not.
///
/// <para><b>Stability:</b> this is the public, versioned plugin contract. A
/// breaking change requires a host-version bump and breaks plugins built
/// against the old shape, so the surface is kept deliberately small. Plugins
/// declare the lowest host version they support via <see cref="MinHostVersion"/>.</para>
///
/// <para><b>Transform hooks</b> mirror Genie 4's <c>ParseText</c>/<c>ParseInput</c>:
/// <see cref="OnGameText"/> and <see cref="OnInput"/> return the (possibly
/// rewritten) string, or <c>null</c> to gag/swallow it. Plugins are chained in
/// registration order — each sees the prior plugin's output. An observe-only
/// plugin simply returns its input unchanged. The other hooks
/// (<see cref="OnXml"/>, <see cref="OnCommandSent"/>, <see cref="OnPrompt"/>,
/// <see cref="OnVariableChanged"/>) are pure observation.</para>
/// </summary>
public interface IGeniePlugin
{
    // ── Identity / metadata ────────────────────────────────────────────────
    /// <summary>Stable, unique id (reverse-dotted), e.g. "genie.exptracker".
    /// Used as the registry key and for enable/disable persistence.</summary>
    string Id { get; }

    string Name        { get; }   // display name
    string Version     { get; }   // the plugin's own version
    string Author      { get; }
    string Description  { get; }

    /// <summary>Lowest Genie 5 host version this plugin supports (semver). The
    /// loader refuses to load a plugin whose minimum exceeds the running host.</summary>
    string MinHostVersion { get; }

    /// <summary>Enable/disable without unloading. Disabled plugins receive no
    /// hook callbacks. Persisted by the host.</summary>
    bool Enabled { get; set; }

    // ── Lifecycle ───────────────────────────────────────────────────────────
    /// <summary>Called once when the plugin is registered/loaded. Capture the
    /// host; do setup. Must not block.</summary>
    void Initialize(IPluginHost host);

    /// <summary>Called when the plugin is unloaded or the session ends. Release
    /// resources and unsubscribe — a leaked handler will pin a DLL plugin's
    /// load context and prevent unload.</summary>
    void Shutdown();

    // ── Transform hooks (Genie 4 ParseText / ParseInput parity) ──────────────
    /// <summary>
    /// A fully-parsed game text line. Return the text to display — unchanged to
    /// observe, modified to rewrite, or <c>null</c> to gag the line entirely.
    /// Plugins are chained in registration order; a <c>null</c> short-circuits
    /// the chain (the line is suppressed). <paramref name="stream"/> is "main"
    /// or a stream id (talk, thoughts, combat, …).
    /// </summary>
    string? OnGameText(string text, string stream);

    /// <summary>
    /// A line of user-typed input, before it is processed. Return the command to
    /// run — unchanged, modified, or <c>null</c> to swallow it. Chained in
    /// registration order (Genie 4 <c>ParseInput</c> parity).
    /// </summary>
    string? OnInput(string input);

    // ── Observation hooks ─────────────────────────────────────────────────────
    /// <summary>A raw XML chunk from the game stream, before/independent of the
    /// parser's typed events — needed for structured data the typed events
    /// don't surface (e.g. <c>&lt;component id='exp Skill'&gt;</c>).</summary>
    void OnXml(string xml);

    /// <summary>A command was actually sent to the game (user, alias, script, or
    /// link click) — observe-only, after <see cref="OnInput"/> transforms.</summary>
    void OnCommandSent(string command);

    /// <summary>A game prompt boundary — a good point to flush batched output.</summary>
    void OnPrompt();

    /// <summary>A tracked session variable changed.</summary>
    void OnVariableChanged(string name, string value);
}
