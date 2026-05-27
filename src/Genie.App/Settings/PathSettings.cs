using System.IO;
using System.Text.Json;

namespace Genie.App.Settings;

/// <summary>
/// User-configurable filesystem paths. Stored as JSON in the app's
/// <c>Config\paths.json</c> file alongside the other settings files.
///
/// At the moment only the Maps directory is configurable, but the same store
/// will likely accumulate scripts/, plugins/, log/, and any other directory
/// the user wants to point at a non-default location. Persistence is a flat
/// JSON object so it's easy to hand-edit and forward-compatible across
/// Genie 5 versions.
/// </summary>
public sealed class PathSettings
{
    /// <summary>
    /// Absolute path to the directory containing zone XML files. Defaults to
    /// a Maps folder next to the Config directory the first time the app
    /// runs; the user can point it at any folder via the File menu — most
    /// notably at a <c>git clone</c> of
    /// <a href="https://github.com/GenieClient/Maps">GenieClient/Maps</a> so
    /// they can contribute updates back via standard git workflow.
    ///
    /// Null / empty means "fall back to the computed default".
    /// </summary>
    public string? MapsDirectory { get; set; }

    // ── JSON persistence ───────────────────────────────────────────────────

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static PathSettings Load(string path)
    {
        if (!File.Exists(path)) return new PathSettings();
        try
        {
            return JsonSerializer.Deserialize<PathSettings>(File.ReadAllText(path), Json)
                   ?? new PathSettings();
        }
        catch
        {
            return new PathSettings();
        }
    }

    public void Save(string path)
        => File.WriteAllText(path, JsonSerializer.Serialize(this, Json));
}
