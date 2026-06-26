namespace Genie.Core.Layout;

/// <summary>
/// Per-window display + routing settings (one instance per registered
/// dockable: Game, Vitals, Room, Backpack, Logons, Talk, …). Edited via the
/// Configuration → Layout tab; persisted per-profile.
///
/// <para>
/// <b>Sentinel-based fallback semantics (Option A):</b> when the user
/// hasn't explicitly overridden a field, the field carries a sentinel
/// value and the consuming code falls back to the global
/// <c>DisplaySettings</c> (or the corresponding
/// <c>Application.Resources</c> key) at render time. Sentinels are:
/// </para>
/// <list type="bullet">
///   <item><see cref="FontFamily"/> = <c>""</c> (empty) → use
///         <c>DisplaySettings.FontFamily</c>.</item>
///   <item><see cref="FontSize"/> = <c>0</c> (non-positive) → use
///         <c>DisplaySettings.FontSize</c>.</item>
///   <item><see cref="Foreground"/> = <c>"Default"</c> or empty → use
///         <c>DisplaySettings.GameColorHex</c>.</item>
///   <item><see cref="Background"/> = <c>""</c> (empty) or <c>"(none)"</c>
///         → transparent (the historical "None" semantic — no
///         global-fallback for background, by design).</item>
/// </list>
/// Resolution is centralised in <c>Genie.App.Controls.WindowSettingsResolver</c>.
/// </summary>
public sealed class WindowSettings
{
    public string  Id           { get; init; } = "";
    public string  DefaultTitle { get; init; } = "";
    public string  DisplayTitle { get; set; } = "";

    /// <summary>Empty string = use global default (sentinel).</summary>
    public string  FontFamily   { get; set; } = "Cascadia Mono,Consolas,Courier New,monospace";

    /// <summary>Non-positive value = use global default (sentinel).</summary>
    public double  FontSize     { get; set; } = 13;

    /// <summary>"Default" or empty = use global default (sentinel).</summary>
    public string  Foreground   { get; set; } = "Default";

    /// <summary>Empty string = transparent (the "None" semantic).</summary>
    public string  Background   { get; set; } = "";

    public bool    Timestamp    { get; set; } = false;

    /// <summary>
    /// Genie 4 "Name List Only" — when true the window only displays lines that
    /// mention a name in the player's Names list (matched via
    /// <c>NameHighlightEngine</c>). A live view filter applied at append time;
    /// scrollback already shown is not retroactively hidden. Toggled from the
    /// window right-click menu and persisted per-window.
    /// </summary>
    public bool    NameListOnly { get; set; } = false;

    public string? IfClosed     { get; set; }
    public event Action? Changed;
    public void NotifyChanged() => Changed?.Invoke();
}
