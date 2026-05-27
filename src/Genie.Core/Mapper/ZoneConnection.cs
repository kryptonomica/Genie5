namespace Genie.Core.Mapper;

/// <summary>
/// One link between two zones — e.g. "board boat at Crossing docks
/// → arrive at Throne City pier after a 5-10 min wait." Lives in a
/// top-level <c>ZoneConnections.xml</c> file in the Maps directory so
/// the community Maps repo can curate it without touching individual
/// zone XML files.
///
/// <para>
/// Used by the multi-zone pathfinder to find routes that span zone
/// boundaries (Crossing → Throne City, etc.). The walker, on reaching
/// a cross-zone node, sends <see cref="Verb"/> and waits for the
/// destination zone's room to become current (subject to
/// <see cref="WaitMin"/> / <see cref="WaitMax"/> for scheduled
/// departures like boats).
/// </para>
/// </summary>
public sealed class ZoneConnection
{
    /// <summary>Unique id for this connection. Used as XML attribute for
    /// stable refs in PRs. Auto-generated if blank on save.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N").Substring(0, 8);

    /// <summary>Source zone file basename without `.xml` extension
    /// ("Map01_Crossing").</summary>
    public string FromZone { get; set; } = string.Empty;

    /// <summary>Source room — either the integer node Id or the DR server
    /// room id (`#NNNN`). Stored as string for forward-compat with the
    /// `<see cref="MapNode.ServerRoomId"/>` indexing strategy.</summary>
    public string FromRoom { get; set; } = string.Empty;

    /// <summary>Destination zone file basename without `.xml` extension.</summary>
    public string ToZone { get; set; } = string.Empty;

    /// <summary>Destination room — same encoding as <see cref="FromRoom"/>.</summary>
    public string ToRoom { get; set; } = string.Empty;

    /// <summary>The verb the walker sends to take this connection
    /// ("board boat", "climb wall", "swim river"). Required.</summary>
    public string Verb { get; set; } = string.Empty;

    /// <summary>Optional type tag for the UI / pathfinder ("boat",
    /// "climb", "ride", "portal", …). Free-form.</summary>
    public string TransitType { get; set; } = string.Empty;

    /// <summary>Skill / class / level requirements — parsed by
    /// <see cref="ExitRequirement.Parse"/> at pathfinding time.</summary>
    public string Requires { get; set; } = string.Empty;

    /// <summary>RT cost in seconds for the transit verb. Null = 0.</summary>
    public int? RtCost { get; set; }

    /// <summary>Lower bound of expected wait in seconds (boat schedule
    /// dwell time, etc.). Null = no wait.</summary>
    public int? WaitMin { get; set; }

    /// <summary>Upper bound of expected wait in seconds. Null = same as
    /// <see cref="WaitMin"/>.</summary>
    public int? WaitMax { get; set; }

    /// <summary>Free-form community notes.</summary>
    public string Notes { get; set; } = string.Empty;

    /// <summary>Average wait time used by the weighted pathfinder.</summary>
    public int AverageWaitSeconds =>
        (WaitMin ?? 0 + (WaitMax ?? WaitMin ?? 0)) / 2;
}
