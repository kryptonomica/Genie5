namespace Genie.Core.Mapper;

public sealed class MapNode
{
    public int    Id           { get; set; }
    public string Title        { get; set; } = string.Empty;
    public string Description  { get; set; } = string.Empty;
    public int    X            { get; set; }
    public int    Y            { get; set; }
    public int    Z            { get; set; }
    public string Notes        { get; set; } = string.Empty;
    public string Color        { get; set; } = string.Empty;
    public string ServerRoomId { get; set; } = string.Empty;
    public List<MapExit> Exits { get; set; } = new();

    /// <summary>
    /// True when this room links to another zone file — its <see cref="Notes"/>
    /// reference a ".xml" map (e.g. <c>"Map31_Riverhaven_East_Gate.xml|E Gate|egate"</c>).
    /// Mirrors Genie 4's <c>Node.IsLabelFile</c> (<c>Note.Contains(".xml")</c>); the
    /// map canvas paints these "cross-zone connector" rooms with a blue border —
    /// the blue boxes that show how one map connects to the next.
    /// </summary>
    public bool IsCrossZone => Notes.Contains(".xml", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Free-form room tags (Lich-style: "bank", "moongate", "gravecottage").
    /// Drive <c>#goto @tag</c> nearest-routing and
    /// <see cref="AutoMapperEngine.FindNearestByTag"/>. Round-tripped as a
    /// '|'-separated <c>tags="..."</c> node attribute — an additive Genie 5
    /// extension that old Genie 4 clients ignore (same as server_id/requires).
    /// </summary>
    public List<string> Tags { get; set; } = new();

    /// <summary>Case-insensitive tag membership test.</summary>
    public bool HasTag(string tag) =>
        Tags.Any(t => t.Equals(tag, StringComparison.OrdinalIgnoreCase));

    public MapExit? GetExit(Direction dir) => Exits.FirstOrDefault(e => e.Direction == dir);

    public MapExit GetOrAddExit(Direction dir, string moveCommand)
    {
        var ex = GetExit(dir);
        if (ex is null) { ex = new MapExit { Direction = dir, MoveCommand = moveCommand }; Exits.Add(ex); }
        return ex;
    }
}
