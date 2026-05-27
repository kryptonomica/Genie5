namespace Genie.Core.Mapper;

/// <summary>
/// On-disk persistence for <see cref="MapZone"/> in the canonical Genie 4 XML
/// format. The Genie 4 XML is the same format used by the
/// <a href="https://github.com/GenieClient/Maps">GenieClient/Maps</a>
/// repository, by Lich, and by every community fork — meaning users can point
/// Genie 5 at a directory that is itself a <c>git clone</c> of the maps repo,
/// edit zones either inside Genie or by hand, and contribute updates back via
/// standard <c>git pull</c> / <c>git push</c> / pull-request workflow without
/// the application being involved in the git mechanics at all.
///
/// Reads delegate to <see cref="Genie4MapImporter"/>; writes delegate to
/// <see cref="Genie4MapExporter"/>. Both honor the schema described on those
/// types, including the Genie 5 <c>server_id</c> extension attribute.
/// </summary>
public sealed class MapZoneRepository
{
    /// <summary>
    /// Write <paramref name="zone"/> to disk as Genie 4 XML at the given path.
    /// Creates the directory if it does not exist. Overwrites without prompt.
    /// </summary>
    public void Save(string path, MapZone zone)
        => Genie4MapExporter.Export(zone, path);

    /// <summary>
    /// Load a Genie 4 XML zone from disk. Returns null when the file is
    /// missing or fails to parse — callers should treat both as "no zone".
    /// </summary>
    public MapZone? Load(string path)
    {
        if (!File.Exists(path)) return null;
        try { return Genie4MapImporter.Import(path); }
        catch { return null; }
    }

    /// <summary>Enumerate every <c>*.xml</c> file in the given directory.</summary>
    public IReadOnlyList<string> ListZoneFiles(string directory)
    {
        if (!Directory.Exists(directory)) return [];
        return Directory.GetFiles(directory, "*.xml");
    }
}
