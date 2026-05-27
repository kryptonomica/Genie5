using Genie.Core.Mapper;

namespace Genie.App.Services;

// WalkStep lives in Genie.Core.Mapper; alias here for brevity in this file.

/// <summary>
/// State of an in-progress auto-walk. One session per Mapper goto request.
/// Owned by <see cref="AutoWalkService"/>; surfaced read-only to the UI
/// so the Mapper status strip can show "Walking to X — N of Y rooms".
/// </summary>
public enum AutoWalkState
{
    /// <summary>Walking — sending moves on each room-change event.</summary>
    Active,

    /// <summary>Halted because the App window has been unfocused for &gt; 60s.
    /// Waiting for the user to click Resume or Cancel. Per
    /// <c>policy_compliance_review.md</c> — attended detection.</summary>
    Paused,

    /// <summary>Stopped — user cancelled, disconnection, or off-path.
    /// Terminal state; the session is no longer the current session.</summary>
    Cancelled,

    /// <summary>Successfully arrived at the destination room. Terminal.</summary>
    Finished,
}

/// <summary>
/// A single auto-walk request. Immutable from the UI's perspective except
/// for mutable progress fields (<see cref="StepsCompleted"/>, <see cref="State"/>,
/// <see cref="CancelReason"/>) which the service updates as the walk runs.
/// </summary>
public sealed class AutoWalkSession
{
    public Guid    Id          { get; } = Guid.NewGuid();
    public MapNode Origin      { get; }
    public MapNode Destination { get; }

    /// <summary>The full ordered list of walk steps produced by the
    /// pathfinder. Each step is either an intra-zone move (just a verb)
    /// or a cross-zone hop carrying wait-time hints.</summary>
    public IReadOnlyList<WalkStep> Plan { get; }

    public int     StepsTotal     { get; }
    public int     StepsCompleted { get; internal set; }
    public AutoWalkState State    { get; internal set; } = AutoWalkState.Active;

    /// <summary>Human-readable reason on Cancelled/Paused — shown in the indicator.</summary>
    public string? StatusMessage  { get; internal set; }

    public DateTimeOffset StartedAt { get; } = DateTimeOffset.Now;

    /// <summary>True if the plan crosses at least one zone boundary —
    /// drives a slightly different indicator ("via boat" etc.).</summary>
    public bool HasCrossZoneHop { get; }

    /// <summary>
    /// Convenience for the indicator: "12 of 27 rooms". Updates as
    /// StepsCompleted advances.
    /// </summary>
    public string ProgressText => $"{StepsCompleted} of {StepsTotal} rooms";

    public AutoWalkSession(MapNode origin, MapNode destination,
                           IReadOnlyList<WalkStep> plan, bool hasCrossZoneHop = false)
    {
        Origin          = origin;
        Destination     = destination;
        Plan            = plan;
        StepsTotal      = plan.Count;
        HasCrossZoneHop = hasCrossZoneHop;
    }

    /// <summary>
    /// Convenience overload — wrap a flat <c>IReadOnlyList&lt;string&gt;</c>
    /// of verbs (the legacy single-zone FindPath result) as WalkSteps.
    /// Used by the single-zone walker so it doesn't need to know about
    /// the cross-zone abstraction.
    /// </summary>
    public AutoWalkSession(MapNode origin, MapNode destination, IReadOnlyList<string> verbs)
        : this(origin, destination,
               verbs.Select(v => new WalkStep { Verb = v }).ToList(),
               hasCrossZoneHop: false)
    { }
}
