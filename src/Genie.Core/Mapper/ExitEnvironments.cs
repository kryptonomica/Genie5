namespace Genie.Core.Mapper;

/// <summary>
/// Suggested values for <see cref="MapExit.Environment"/> — the physical aid or
/// transit an exit uses. Populates the Edit Exit dialog's Environment dropdown,
/// grouping the timing fields (RT cost, wait window) under a recognisable label.
/// The list is a convenience, not a constraint: the field is free-text, so an
/// author can enter anything not covered here (e.g. "Raft", "Drawbridge").
/// </summary>
public static class ExitEnvironments
{
    /// <summary>Common traversal kinds, alphabetical.</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        "Boat", "Bridge", "Climb", "Door", "Ferry", "Ford", "Gate", "Ladder",
        "Portal", "Rope", "Stairs", "Swim", "Trail", "Tunnel", "Wade",
    };
}
