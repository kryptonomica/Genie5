namespace Genie.Core.Mapper;

public sealed class MapZone
{
    public Guid   Id       { get; set; } = Guid.NewGuid();
    public string Name     { get; set; } = "Zone 1";
    public string Genie4Id { get; set; } = string.Empty;
    public Dictionary<int, MapNode> Nodes { get; set; } = new();
}
