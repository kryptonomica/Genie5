namespace Genie.Core.Mapper;

public sealed class MapZone
{
    public Guid   Id       { get; set; } = Guid.NewGuid();
    public string Name     { get; set; } = "Zone 1";
    public string Genie4Id { get; set; } = string.Empty;
    public Dictionary<int, MapNode> Nodes { get; set; } = new();

    /// <summary>
    /// Free-floating text labels (Genie 4 <c>&lt;label&gt;</c> elements) — landmark
    /// names drawn on the map for orientation. Imported from / exported to the
    /// zone XML so they round-trip; rendered by the map canvas in black at their
    /// stored positions, exactly like Genie 4's AutoMapper.
    /// </summary>
    public List<MapLabel> Labels { get; set; } = new();
}
