using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace Genie.App.Services;

/// <summary>
/// Fetches and caches DR room/scene artwork. DR sends a numeric picture id via
/// <c>&lt;resource picture="N"/&gt;</c>; the official client resolves it to a JPG
/// on play.net's BFE art CDN. We mirror that, caching each image under the
/// configured art dir so a given room's art is downloaded only once.
///
/// <para>Source URL: <c>https://www.play.net/bfe/{game}-art/{id}.jpg</c> —
/// first-party Simutronics art (the same asset the Wrayth client loads), so
/// there's no third-party data leaving the machine.</para>
/// </summary>
public sealed class RoomArtService
{
    // One shared client for the process — HttpClient is designed to be reused.
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(15),
    };

    private readonly string _artDir;
    private readonly string _gameCode;

    public RoomArtService(string artDir, string gameCode = "DR")
    {
        _artDir   = artDir;
        _gameCode = string.IsNullOrWhiteSpace(gameCode) ? "DR" : gameCode.Trim().ToUpperInvariant();
    }

    /// <summary>
    /// Resolve a picture id to a local cached JPG path, downloading it on first
    /// use. Returns null if the id is empty/"0" or the fetch fails — art is
    /// best-effort and must never throw into the UI.
    /// </summary>
    public async Task<string?> GetImagePathAsync(string pictureId)
    {
        if (string.IsNullOrWhiteSpace(pictureId) || pictureId == "0") return null;

        // Sanitize: ids are numeric in practice; guard against anything that
        // could escape the cache dir before using the id as a file name.
        var safeId = Path.GetFileName(pictureId.Trim());
        if (safeId.Length == 0) return null;

        var cacheDir = Path.Combine(_artDir, _gameCode);
        var cached   = Path.Combine(cacheDir, safeId + ".jpg");
        if (File.Exists(cached)) return cached;

        try
        {
            var url = $"https://www.play.net/bfe/{_gameCode}-art/{safeId}.jpg";
            var bytes = await Http.GetByteArrayAsync(url).ConfigureAwait(false);
            if (bytes.Length == 0) return null;

            Directory.CreateDirectory(cacheDir);
            await File.WriteAllBytesAsync(cached, bytes).ConfigureAwait(false);
            return File.Exists(cached) ? cached : null;
        }
        catch
        {
            return null;   // network/404/IO — no art this time, try again next visit
        }
    }
}
