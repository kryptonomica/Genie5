namespace Genie.Core.Mapper;

/// <summary>
/// A free-floating text label on a map (Genie 4 <c>&lt;label&gt;</c> element) —
/// landmark names like "East Gate", "Guard House", "Driftwood Designs" that the
/// map author placed for human orientation. Distinct from a node's
/// <see cref="MapNode.Notes"/> (which are #goto aliases, not drawn on the map).
///
/// Positions are stored in grid units (Genie 4 pixels ÷ 20), matching
/// <see cref="MapNode.X"/>/<see cref="MapNode.Y"/>, so labels share the node
/// coordinate space and render against the same origin.
/// </summary>
public sealed class MapLabel
{
    public string Text { get; set; } = string.Empty;
    public int    X    { get; set; }
    public int    Y    { get; set; }
    public int    Z    { get; set; }
}
