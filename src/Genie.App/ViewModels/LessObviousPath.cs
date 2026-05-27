namespace Genie.App.ViewModels;

/// <summary>
/// One non-compass arc from the current room, surfaced as a button in the
/// Mapper panel's "Less Obvious Paths" strip. Backed by a
/// <see cref="Genie.Core.Mapper.MapExit"/> with <c>Direction == None</c> —
/// typically "go &lt;noun&gt;", "climb &lt;noun&gt;", "swim &lt;noun&gt;",
/// "jump &lt;noun&gt;", etc.
///
/// <see cref="RequiresHint"/> is a forward-compatible field: the upstream
/// Genie 4 XML format doesn't carry skill / class / level requirements on
/// arcs, but we've added an optional <c>requires=</c> attribute that the
/// community can populate over time. When present, it shows up here so the
/// UI can render a tooltip ("requires athletics 50") and (eventually) grey
/// out arcs the current character can't take.
/// </summary>
public sealed class LessObviousPath
{
    /// <summary>Raw command to send to the game — e.g. "go small alleyway".</summary>
    public string MoveCommand    { get; }

    /// <summary>
    /// Skill / class / level requirement hint. Empty when the arc has no
    /// declared requirement. Format intentionally loose for now — see
    /// <see cref="Genie.Core.Mapper.MapExit.Requires"/> for the conventions
    /// the community is starting to settle on.
    /// </summary>
    public string RequiresHint   { get; }

    /// <summary>True iff <see cref="RequiresHint"/> has content.</summary>
    public bool   HasRequirement { get; }

    public LessObviousPath(string moveCommand, string requiresHint)
    {
        MoveCommand    = moveCommand;
        RequiresHint   = requiresHint;
        HasRequirement = !string.IsNullOrEmpty(requiresHint);
    }
}
