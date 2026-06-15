namespace Genie.Core.Mapper;

public sealed class MapExit
{
    public Direction Direction     { get; set; }
    public string    MoveCommand   { get; set; } = string.Empty;
    public int?      DestinationId { get; set; }

    /// <summary>
    /// Free-form skill / class / level requirement hint for non-compass arcs
    /// ("climb tall wall", "swim raging river", "go secret door", etc.).
    /// Parsed by <see cref="ExitRequirement"/> into structured form
    /// (min ranks, required class, min level). Expected shapes:
    /// <list type="bullet">
    ///   <item><c>"athletics 50"</c> — legacy free-form</item>
    ///   <item><c>"climbing&gt;=50, athletics&gt;=30"</c> — explicit min</item>
    ///   <item><c>"class=Thief"</c> — guild restriction</item>
    ///   <item><c>"level&gt;=25"</c> — character level gate</item>
    /// </list>
    /// Old Genie 4 clients ignore the round-tripped <c>requires=</c>
    /// attribute. Genie 5 surfaces it as a tooltip on Less Obvious Paths
    /// buttons and uses it for skill-weighted Dijkstra in
    /// <see cref="AutoMapperEngine.FindPath"/>.
    /// </summary>
    public string Requires { get; set; } = string.Empty;

    /// <summary>
    /// Roundtime cost in seconds for taking this exit. Used by the
    /// weighted pathfinder to prefer faster routes when multiple paths
    /// exist. Null = unknown (treated as 0 by the pathfinder).
    /// </summary>
    public int? RtCost { get; set; }

    /// <summary>
    /// Lower bound of expected wait time in seconds. Used for boats and
    /// other scheduled departures: "boards every 5-10 minutes" → 300.
    /// Null = no wait (immediate transit).
    /// </summary>
    public int? WaitMin { get; set; }

    /// <summary>
    /// Upper bound of expected wait time in seconds. Pathfinder averages
    /// <see cref="WaitMin"/> + <see cref="WaitMax"/> when computing edge
    /// weight. Null = same as WaitMin (deterministic wait).
    /// </summary>
    public int? WaitMax { get; set; }

    /// <summary>
    /// Physical aid / transit kind this exit uses — "Bridge", "Boat", "Rope",
    /// "Ladder", "Ford", "Climb", etc. (see
    /// <see cref="ExitEnvironments"/>). Descriptive metadata that groups the
    /// timing fields (<see cref="RtCost"/> / <see cref="WaitMin"/>) under a
    /// recognisable label in the Edit Exit dialog and lets the community
    /// classify how a link is traversed. Empty = an ordinary walked step.
    /// Round-tripped as the <c>env</c> attribute; old Genie 4 clients ignore it.
    /// </summary>
    public string Environment { get; set; } = string.Empty;

    /// <summary>
    /// Free-form notes from the community Maps repo: prerequisites the
    /// pathfinder can't model ("rope needed", "only at night", etc.).
    /// Surfaces as a tooltip on the Less Obvious Paths button.
    /// </summary>
    public string Notes { get; set; } = string.Empty;
}
