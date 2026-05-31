namespace Genie.Core.Update.Sources;

/// <summary>
/// A remote that exposes its content as a list of independently-downloadable
/// files — the GitHub Contents API and any "manifest-with-files-array" HTTP
/// endpoint fit this shape. Used by the Maps updater (and any future
/// scripts/art updater) because those domains are loose-files-in-a-folder,
/// not a single release blob.
///
/// Contrast with the (planned) IReleaseSource which models the
/// one-asset-per-version shape used for the Core app and plugin DLLs.
/// </summary>
public interface IFileListSource
{
    /// <summary>Human-readable identity for logs and the Updates dialog (e.g. "GenieClient/Maps").</summary>
    string Description { get; }

    /// <summary>Fetch the current listing — fast, no payload bytes downloaded.</summary>
    Task<FileListInfo> GetFileListAsync(CancellationToken ct = default);

    /// <summary>
    /// Download a single file's bytes. The caller is responsible for any
    /// "should I bother?" sha comparison via <see cref="FileEntry.Sha"/>
    /// before invoking this method.
    /// </summary>
    Task<byte[]> DownloadFileAsync(FileEntry file, CancellationToken ct = default);
}

/// <summary>
/// Snapshot of a file-list source at one point in time.
/// </summary>
/// <param name="Identity">
///   Source-specific identity string for the snapshot — for GitHub Contents
///   this is typically just the source description (the listing endpoint
///   doesn't return a tree sha unless we hit /git/trees, which we don't).
///   Used for logs only; individual file integrity is per-entry via
///   <see cref="FileEntry.Sha"/>.</param>
/// <param name="Files">Files available for download.</param>
public sealed record FileListInfo(
    string                     Identity,
    IReadOnlyList<FileEntry>  Files);

/// <summary>One downloadable file in a file-list source.</summary>
/// <param name="Name">Filename (no path components — files live in a single logical folder per source).</param>
/// <param name="DownloadUrl">Direct URL to the raw bytes.</param>
/// <param name="Sha">
///   Optional integrity / change-detection hash. For GitHub Contents this is
///   the git blob sha-1 (computable locally via <c>git hash-object</c> — see
///   <see cref="GithubContentsSource.GitBlobSha1"/>). For other sources it
///   may be a SHA-256 from a published manifest, or null when the source
///   doesn't expose a hash.</param>
/// <param name="Size">Byte size if reported by the listing, else null.</param>
public sealed record FileEntry(
    string  Name,
    string  DownloadUrl,
    string? Sha,
    long?   Size);
