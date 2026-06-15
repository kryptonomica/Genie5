using System.Text;
using System.Xml;

namespace Genie.Core.Mapper;

/// <summary>
/// Writes a <see cref="MapZone"/> as Genie 4 compatible XML. The output matches
/// the schema consumed by <see cref="Genie4MapImporter"/> so round-tripping a
/// zone through Genie 5 does not break compatibility with Genie 4, Lich, or
/// the official <c>GenieClient/Maps</c> repository.
///
/// Format notes:
/// <list type="bullet">
///   <item>UTF-8 without BOM, LF line endings, 2-space indent — matches the
///         conventions in the upstream repo so <c>git diff</c> output stays
///         minimal across forks and platforms.</item>
///   <item>Position values are scaled back to pixel coordinates (X * 20, Y * 20)
///         — Genie 4 stores pixels; we work in grid units internally.</item>
///   <item>Nodes are emitted in ascending node-id order so two clients walking
///         the same zone produce identical files.</item>
///   <item>The <c>server_id</c> attribute is a Genie 5 extension carrying the
///         server's room id from <c>&lt;nav rm="..."/&gt;</c>. Old clients
///         ignore it; new clients use it for instant room matching. Including
///         it in PRs lets the community accumulate the server-id → node-id
///         mapping over time.</item>
/// </list>
/// </summary>
public static class Genie4MapExporter
{
    /// <summary>Serialize a zone to a file at <paramref name="xmlPath"/>.</summary>
    public static void Export(MapZone zone, string xmlPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(xmlPath)!);
        File.WriteAllText(xmlPath, Serialize(zone), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    /// <summary>Serialize a zone to a string. Useful for tests and previews.</summary>
    public static string Serialize(MapZone zone)
    {
        var sb = new StringBuilder();
        var settings = new XmlWriterSettings
        {
            Indent             = true,
            IndentChars        = "  ",
            NewLineChars       = "\n",
            NewLineHandling    = NewLineHandling.Replace,
            OmitXmlDeclaration = false,
            Encoding           = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        };

        using (var writer = XmlWriter.Create(sb, settings))
        {
            writer.WriteStartDocument();
            writer.WriteStartElement("zone");
            writer.WriteAttributeString("name", zone.Name);
            if (!string.IsNullOrEmpty(zone.Genie4Id))
                writer.WriteAttributeString("id", zone.Genie4Id);

            // Sort by node id so two clients walking the same zone produce
            // byte-identical output — critical for git-friendly diffs.
            foreach (var nodeKv in zone.Nodes.OrderBy(kv => kv.Key))
                WriteNode(writer, nodeKv.Value);

            writer.WriteEndElement();   // zone
            writer.WriteEndDocument();
        }

        return sb.ToString();
    }

    private static void WriteNode(XmlWriter writer, MapNode node)
    {
        writer.WriteStartElement("node");
        // Attribute order matches the importer's expectations and Genie 4's
        // hand-edited convention: id, name, note, color, server_id.
        writer.WriteAttributeString("id",   node.Id.ToString());
        writer.WriteAttributeString("name", node.Title);
        if (!string.IsNullOrEmpty(node.Notes))        writer.WriteAttributeString("note",      node.Notes);
        if (!string.IsNullOrEmpty(node.Color))        writer.WriteAttributeString("color",     node.Color);
        if (!string.IsNullOrEmpty(node.ServerRoomId)) writer.WriteAttributeString("server_id", node.ServerRoomId);
        // tags — Genie 5 extension, '|'-separated. Sorted (case-insensitive) so
        // two clients emit byte-identical output for git-friendly diffs. Omitted
        // when empty so unchanged upstream maps produce no diff.
        if (node.Tags.Count > 0)
        {
            var tags = string.Join('|', node.Tags.Select(t => t.Trim())
                                                  .Where(t => t.Length > 0)
                                                  .OrderBy(t => t, StringComparer.OrdinalIgnoreCase));
            if (tags.Length > 0) writer.WriteAttributeString("tags", tags);
        }

        if (!string.IsNullOrEmpty(node.Description))
        {
            writer.WriteStartElement("description");
            writer.WriteString(node.Description);
            writer.WriteEndElement();
        }

        // Position — scale grid units back to pixels (importer divides by 20).
        writer.WriteStartElement("position");
        writer.WriteAttributeString("x", (node.X * 20).ToString());
        writer.WriteAttributeString("y", (node.Y * 20).ToString());
        writer.WriteAttributeString("z", node.Z.ToString());
        writer.WriteEndElement();

        // Arcs: write in declared order so consecutive walks of the same room
        // produce identical files. We preserve the move command verbatim when
        // present — it may contain shop/climb/jump commands distinct from the
        // bare exit name.
        foreach (var exit in node.Exits)
        {
            writer.WriteStartElement("arc");
            writer.WriteAttributeString("exit", exit.Direction.ToString().ToLowerInvariant());
            writer.WriteAttributeString("move",
                string.IsNullOrEmpty(exit.MoveCommand) ? exit.Direction.ToString().ToLowerInvariant() : exit.MoveCommand);
            if (exit.DestinationId.HasValue)
                writer.WriteAttributeString("destination", exit.DestinationId.Value.ToString());
            // Genie 5 extensions. Omitted when null/empty so PR diffs
            // against unchanged upstream maps stay minimal. Old Genie 4
            // clients silently ignore unknown attributes — backwards
            // compatible round-trip.
            if (!string.IsNullOrEmpty(exit.Requires))
                writer.WriteAttributeString("requires", exit.Requires);
            if (exit.RtCost.HasValue)
                writer.WriteAttributeString("rt", exit.RtCost.Value.ToString());
            if (exit.WaitMin.HasValue)
                writer.WriteAttributeString("wait_min", exit.WaitMin.Value.ToString());
            if (exit.WaitMax.HasValue)
                writer.WriteAttributeString("wait_max", exit.WaitMax.Value.ToString());
            if (!string.IsNullOrEmpty(exit.Environment))
                writer.WriteAttributeString("env", exit.Environment);
            if (!string.IsNullOrEmpty(exit.Notes))
                writer.WriteAttributeString("notes", exit.Notes);
            writer.WriteEndElement();
        }

        writer.WriteEndElement();   // node
    }
}
