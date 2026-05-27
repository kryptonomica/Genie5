using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Genie.Core.Mapper;

/// <summary>
/// Pulls zone XML files from the public
/// <a href="https://github.com/GenieClient/Maps">GenieClient/Maps</a>
/// repository, imports each into our JSON zone format, and merges the
/// fresh structure with whatever is already on disk so locally-collected
/// per-node data (server-side room ids the player has visited, primarily)
/// survives the refresh.
///
/// Equivalent to Genie4's "Update Maps" command, which delegates the work
/// to a separate Lamp.exe updater. We do it inline against the GitHub REST
/// API so there's nothing extra to install.
/// </summary>
public sealed class MapRepoUpdater
{
    private const string ListingUrl =
        "https://api.github.com/repos/GenieClient/Maps/contents/";
    private const string UserAgent = "Genie5-MapUpdater";

    private readonly HttpClient _http;
    private readonly MapZoneRepository _repo;
    private readonly string _mapsDir;

    public MapRepoUpdater(MapZoneRepository repo, string mapsDir)
    {
        _repo    = repo;
        _mapsDir = mapsDir;

        // Long timeout — the listing pull is small but downloading every
        // .xml in series can run up against default 100s on a slow link.
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        _http.DefaultRequestHeaders.Accept.Clear();
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
    }

    /// <summary>
    /// Fetch the repo's directory listing, download every <c>*.xml</c>
    /// found, parse each, and merge with any existing local zone XML of
    /// the same name. The merge preserves <see cref="MapNode.ServerRoomId"/>
    /// values per node-id so the player's accumulated room-id mapping isn't
    /// lost when the upstream zone refreshes its structure. The merged zone
    /// is written back as Genie 4 XML (same format the upstream repo uses)
    /// so a Maps directory that is itself a <c>git clone</c> stays cleanly
    /// diffable.
    /// </summary>
    /// <param name="progress">Optional callback invoked once per file with
    ///   <c>(currentIndex, totalFiles, filename, status)</c>. Status is one
    ///   of <c>"downloading"</c>, <c>"merged"</c>, <c>"new"</c>,
    ///   <c>"failed: …"</c>.</param>
    /// <returns>Result summary with counts and any per-file errors.</returns>
    public async Task<UpdateResult> UpdateAllAsync(
        Action<int,int,string,string>? progress = null,
        CancellationToken cancel = default)
    {
        Directory.CreateDirectory(_mapsDir);

        var listingJson = await _http.GetStringAsync(ListingUrl, cancel);
        var entries = JsonSerializer.Deserialize<List<RepoEntry>>(listingJson) ?? new();
        var xmls = entries
            .Where(e => e.Type == "file" &&
                        e.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
                        !string.IsNullOrEmpty(e.DownloadUrl))
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var result = new UpdateResult { TotalFiles = xmls.Count };

        for (int i = 0; i < xmls.Count; i++)
        {
            cancel.ThrowIfCancellationRequested();
            var entry = xmls[i];
            progress?.Invoke(i + 1, xmls.Count, entry.Name, "downloading");

            try
            {
                var xml      = await _http.GetStringAsync(entry.DownloadUrl, cancel);
                var fallback = Path.GetFileNameWithoutExtension(entry.Name);
                var fresh    = Genie4MapImporter.ImportFromContent(xml, fallback);

                // Use the upstream filename (not the zone's <zone name="..."/>
                // attribute) so PR-friendly identity matches the upstream repo
                // exactly. Two zones with the same display name but different
                // filenames stay distinct on disk.
                var localPath = Path.Combine(_mapsDir, entry.Name);
                bool isNew    = !File.Exists(localPath);
                if (!isNew)
                {
                    var existing = _repo.Load(localPath);
                    if (existing != null)
                    {
                        MergePreservingServerIds(existing, fresh);
                        result.Merged++;
                    }
                    else
                    {
                        // File on disk but unreadable — treat as a fresh import.
                        result.New++;
                    }
                }
                else
                {
                    result.New++;
                }

                _repo.Save(localPath, fresh);
                progress?.Invoke(i + 1, xmls.Count, entry.Name, isNew ? "new" : "merged");
            }
            catch (Exception ex)
            {
                result.Failures.Add((entry.Name, ex.Message));
                progress?.Invoke(i + 1, xmls.Count, entry.Name, $"failed: {ex.Message}");
            }
        }

        return result;
    }

    /// <summary>
    /// Copy locally-collected per-node fields from <paramref name="existing"/>
    /// onto <paramref name="fresh"/>, keyed on Genie4 node id. Right now
    /// that's just <see cref="MapNode.ServerRoomId"/> — populated when the
    /// player has visited the room and seen its <c>&lt;nav rm="…"/&gt;</c>.
    /// Everything else (title, description, exits, coordinates, notes) is
    /// taken from the upstream refresh, since that's what the user wanted
    /// to update.
    /// </summary>
    private static void MergePreservingServerIds(MapZone existing, MapZone fresh)
    {
        foreach (var (id, freshNode) in fresh.Nodes)
        {
            if (existing.Nodes.TryGetValue(id, out var existingNode) &&
                !string.IsNullOrEmpty(existingNode.ServerRoomId))
            {
                freshNode.ServerRoomId = existingNode.ServerRoomId;
            }
        }
    }

    /// <summary>
    /// Pull the helper scripts from the repo's
    /// <c>Copy These to Genie's Scripts Folder</c> subdirectory and write
    /// any that are missing or have changed in the upstream repo into the
    /// caller's <paramref name="scriptsDir"/>. The "is changed" check
    /// compares the git blob sha returned by the contents API against
    /// the same hash computed locally, so untouched files don't even
    /// trigger a network round-trip beyond the directory listing.
    /// </summary>
    public async Task<ScriptUpdateResult> UpdateScriptsAsync(
        string scriptsDir,
        Action<int,int,string,string>? progress = null,
        CancellationToken cancel = default)
    {
        Directory.CreateDirectory(scriptsDir);

        const string folder = "Copy%20These%20to%20Genie%27s%20Scripts%20Folder";
        var listingJson = await _http.GetStringAsync(
            $"https://api.github.com/repos/GenieClient/Maps/contents/{folder}", cancel);
        var entries = JsonSerializer.Deserialize<List<RepoEntry>>(listingJson) ?? new();
        var files = entries
            .Where(e => e.Type == "file" && !string.IsNullOrEmpty(e.DownloadUrl))
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var result = new ScriptUpdateResult { TotalFiles = files.Count };

        for (int i = 0; i < files.Count; i++)
        {
            cancel.ThrowIfCancellationRequested();
            var entry = files[i];
            try
            {
                var localPath = Path.Combine(scriptsDir, entry.Name);
                var status    = "checking";
                progress?.Invoke(i + 1, files.Count, entry.Name, status);

                if (File.Exists(localPath) &&
                    !string.IsNullOrEmpty(entry.Sha) &&
                    GitBlobSha1(localPath) == entry.Sha)
                {
                    result.Unchanged++;
                    progress?.Invoke(i + 1, files.Count, entry.Name, "unchanged");
                    continue;
                }

                var content = await _http.GetByteArrayAsync(entry.DownloadUrl, cancel);
                var isNew   = !File.Exists(localPath);
                await File.WriteAllBytesAsync(localPath, content, cancel);

                if (isNew) result.New++; else result.Updated++;
                progress?.Invoke(i + 1, files.Count, entry.Name, isNew ? "new" : "updated");
            }
            catch (Exception ex)
            {
                result.Failures.Add((entry.Name, ex.Message));
                progress?.Invoke(i + 1, files.Count, entry.Name, $"failed: {ex.Message}");
            }
        }

        return result;
    }

    /// <summary>
    /// Compute the git blob sha-1 for a local file. Git's blob hashing
    /// prepends a header — <c>"blob &lt;size&gt;\0"</c> — and hashes the
    /// concatenation. Matching what GitHub returns as <c>sha</c> on its
    /// contents API lets us cheaply skip writes for unchanged files.
    /// </summary>
    private static string GitBlobSha1(string path)
    {
        var content = File.ReadAllBytes(path);
        var header  = System.Text.Encoding.ASCII.GetBytes($"blob {content.Length}\0");
        using var sha = System.Security.Cryptography.SHA1.Create();
        sha.TransformBlock(header, 0, header.Length, null, 0);
        sha.TransformFinalBlock(content, 0, content.Length);
        return Convert.ToHexString(sha.Hash!).ToLowerInvariant();
    }

    public sealed class UpdateResult
    {
        public int TotalFiles { get; set; }
        public int Merged     { get; set; }
        public int New        { get; set; }
        public List<(string Name, string Error)> Failures { get; } = new();
    }

    public sealed class ScriptUpdateResult
    {
        public int TotalFiles { get; set; }
        public int Updated    { get; set; }
        public int New        { get; set; }
        public int Unchanged  { get; set; }
        public List<(string Name, string Error)> Failures { get; } = new();
    }

    private sealed class RepoEntry
    {
        [JsonPropertyName("name")]
        public string Name        { get; set; } = "";
        [JsonPropertyName("type")]
        public string Type        { get; set; } = "";
        [JsonPropertyName("download_url")]
        public string? DownloadUrl { get; set; }
        // Git blob sha returned by /contents/. Matches what `git hash-object`
        // produces for the same content; we compare against the local file
        // to skip writes for unchanged scripts.
        [JsonPropertyName("sha")]
        public string? Sha        { get; set; }
    }
}
