namespace Genie.Core.Update;

/// <summary>
/// Top-level on-disk schema for the update system. Loaded from
/// <c>{ConfigDir}/update-feeds.json</c> via <see cref="FeedConfigStore"/>;
/// missing files are seeded with <see cref="CreateDefault"/>.
///
/// Three sections — Core, Maps, Plugins — match the three update kinds.
/// Core is a single feed (you can only run one Genie 5 install at a time);
/// Maps and Plugins are lists so users can subscribe to multiple sources.
/// </summary>
public sealed class FeedConfig
{
    public CoreFeed         Core    { get; set; } = CoreFeed.Default();
    public List<FeedEntry>  Maps    { get; set; } = new();
    public List<FeedEntry>  Plugins { get; set; } = new();

    /// <summary>Out-of-the-box defaults: official Genie5 core, official Maps repo, official Experience plugin.</summary>
    public static FeedConfig CreateDefault() => new()
    {
        Core    = CoreFeed.Default(),
        Maps    = new() { FeedEntry.OfficialMaps()       },
        Plugins = new() { FeedEntry.OfficialExpTracker() },
    };
}

/// <summary>
/// Settings for the Core App updater. Only one Core feed exists at a time
/// (you don't subscribe to multiple Genie 5 builds — you pick a channel).
/// </summary>
public sealed class CoreFeed
{
    /// <summary>Source kind; today only <c>"github-releases"</c> is supported for Core.</summary>
    public string Kind         { get; set; } = "github-releases";
    public string Owner        { get; set; } = "GenieClient";
    public string Repo         { get; set; } = "Genie5";

    /// <summary>Release channel — <c>"stable"</c> or <c>"beta"</c>. Beta = GitHub releases marked prerelease.</summary>
    public string Channel      { get; set; } = "stable";

    /// <summary>
    ///   Asset filename pattern. Supports <c>{os}</c> (win/osx/linux) and
    ///   <c>{arch}</c> (x64/arm64) placeholders; the Core updater fills them
    ///   in from the runtime before matching against release assets.</summary>
    public string AssetPattern { get; set; } = "Genie5-{os}-{arch}.zip";

    public bool CheckOnStartup { get; set; } = true;

    public static CoreFeed Default() => new();
}

/// <summary>One entry in the Maps or Plugins feeds list.</summary>
public sealed class FeedEntry
{
    /// <summary>Stable identifier for this entry — never changes, used as a dictionary key.</summary>
    public string Id            { get; set; } = "";

    /// <summary>Display name shown in the Updates dialog.</summary>
    public string Name          { get; set; } = "";

    /// <summary>
    ///   Source kind — drives how the entry is materialised into an
    ///   <see cref="Sources.IFileListSource"/> or future IReleaseSource.
    ///   Today: <c>"github-contents"</c> (Maps), <c>"github-releases"</c>
    ///   (Plugins, Phase 2), <c>"http-manifest"</c> (Phase 2).</summary>
    public string Kind          { get; set; } = "";

    // ── github-* common fields ───────────────────────────────────────────
    public string Owner         { get; set; } = "";
    public string Repo          { get; set; } = "";

    /// <summary>Optional subdirectory for github-contents sources; root if blank.</summary>
    public string Path          { get; set; } = "";

    /// <summary>Optional file extension filter for github-contents sources (e.g. <c>".xml"</c>).</summary>
    public string Extension     { get; set; } = "";

    /// <summary>Asset filename pattern for github-releases sources; supports same placeholders as <see cref="CoreFeed.AssetPattern"/>.</summary>
    public string AssetPattern  { get; set; } = "";

    // ── http-manifest fields ─────────────────────────────────────────────
    public string ManifestUrl   { get; set; } = "";

    // ── common state ─────────────────────────────────────────────────────
    public bool             Enabled     { get; set; } = true;
    public DateTimeOffset?  LastChecked { get; set; }

    /// <summary>The default GenieClient/Maps repo (XML-files-in-root, .xml-filtered).</summary>
    public static FeedEntry OfficialMaps() => new()
    {
        Id        = "official-maps",
        Name      = "GenieClient/Maps (official)",
        Kind      = "github-contents",
        Owner     = "GenieClient",
        Repo      = "Maps",
        Extension = ".xml",
        Enabled   = true,
    };

    /// <summary>The default Plugin_EXPTrackerV5 release feed (single DLL asset).</summary>
    public static FeedEntry OfficialExpTracker() => new()
    {
        Id           = "official-exptracker",
        Name         = "Plugin_EXPTrackerV5 (official)",
        Kind         = "github-releases",
        Owner        = "GenieClient",
        Repo         = "Plugin_EXPTrackerV5",
        AssetPattern = "Plugin_EXPTrackerV5.dll",
        Enabled      = true,
    };
}
