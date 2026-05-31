namespace Genie.Core.Update.Sources;

/// <summary>
/// A remote that publishes its content as discrete versioned releases — each
/// release has a SemVer tag and one or more downloadable assets. Models the
/// shape GitHub Releases (and most modern release pipelines) use; contrasted
/// with <see cref="IFileListSource"/> which models loose-files-in-a-folder.
///
/// Used by the Core App updater (Phase 4 — via Velopack as the underlying
/// transport, but conceptually the same shape) and the Plugin updater
/// (Phase 2 — straight GitHub Releases).
/// </summary>
public interface IReleaseSource
{
    /// <summary>Human-readable identity for logs and the Updates dialog.</summary>
    string Description { get; }

    /// <summary>
    /// Fetch the latest release on the given channel — fast, no asset bytes
    /// downloaded. Returns null when no release matches (e.g. a new repo with
    /// no releases yet, or asking for "stable" when only prereleases exist).
    /// </summary>
    /// <param name="channel">
    ///   <c>"stable"</c> (latest non-prerelease) or <c>"beta"</c> (latest
    ///   prerelease, falling through to stable if no prereleases exist).</param>
    Task<ReleaseInfo?> GetLatestReleaseAsync(string channel = "stable", CancellationToken ct = default);

    /// <summary>
    /// Download the bytes of a single asset within a release. Caller chooses
    /// the asset (typically via <see cref="ResolveAsset"/> or a pattern match
    /// on <see cref="ReleaseInfo.Assets"/>).
    /// </summary>
    Task<byte[]> DownloadAssetAsync(ReleaseAsset asset, CancellationToken ct = default);
}

/// <summary>
/// Snapshot of one release. <see cref="Version"/> is the source's tag string —
/// typically SemVer with an optional leading <c>v</c> (e.g. <c>v1.2.3</c> or
/// <c>5.0.0-alpha.2</c>). Use <see cref="VersionComparer"/> to compare safely;
/// don't string-compare directly.
/// </summary>
public sealed record ReleaseInfo(
    string                       Version,
    string?                      Name,
    string?                      Notes,
    DateTimeOffset               PublishedAt,
    bool                         IsPrerelease,
    IReadOnlyList<ReleaseAsset>  Assets);

/// <summary>One downloadable file attached to a release.</summary>
public sealed record ReleaseAsset(
    string  Name,
    string  DownloadUrl,
    long    Size,
    string? ContentType);
