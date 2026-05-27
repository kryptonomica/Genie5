namespace Genie.Core.Mapper;

/// <summary>
/// Canonicalises a room down to a deterministic string key from its title and
/// available exits. Used by <see cref="AutoMapperEngine"/> for in-zone room
/// matching and by the app layer for cross-zone auto-detection (which zone
/// contains a room with this fingerprint?).
/// </summary>
public static class MapFingerprint
{
    public static string Compute(string title, IEnumerable<string> exits)
    {
        var canonical = exits
            .Select(DirectionHelper.Parse)
            .Where(d => d != Direction.None)
            .Select(d => d.ToString().ToLowerInvariant())
            .Distinct()
            .Order(StringComparer.OrdinalIgnoreCase);
        return $"{title.Trim()}|{string.Join(",", canonical)}";
    }

    public static string Compute(string title, IEnumerable<MapExit> exits)
        => Compute(title, exits.Select(e => e.Direction.ToString()));
}
