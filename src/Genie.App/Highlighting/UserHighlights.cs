using Genie.Core.Diagnostics;
using Genie.Core.Highlights;

namespace Genie.App.Highlighting;

/// <summary>
/// Process-wide pointer to the active <see cref="HighlightEngine"/>. Set by
/// <c>MainWindowViewModel</c> when a <c>GenieCore</c> connects, cleared on
/// disconnect. <see cref="DefaultHighlights"/> consults this on every line
/// to layer user-defined highlights on top of the built-in defaults.
///
/// A static singleton is the pragmatic choice here: the renderer template
/// can't easily reach the MainWindowViewModel, and we only ever have one
/// active session at a time.
/// </summary>
public static class UserHighlights
{
    public static HighlightEngine? Engine { get; set; }

    /// <summary>Active session's metrics collector, set alongside <see cref="Engine"/>.
    /// <see cref="DefaultHighlights"/> times the user-highlight pass into the
    /// Highlights stage through this. Null when no session is connected.</summary>
    public static PipelineMetrics? Metrics { get; set; }

    /// <summary>
    /// Raised after any change to the active engine's rules. The game-text
    /// view-model subscribes to this and force-re-tokenizes its already-
    /// rendered lines so the new highlight set repaints visible text, not
    /// just lines that arrive after the change.
    /// </summary>
    public static event Action? RulesChanged;

    /// <summary>Call from the Configuration dialog after Apply / Remove / Load.</summary>
    public static void NotifyRulesChanged() => RulesChanged?.Invoke();
}
