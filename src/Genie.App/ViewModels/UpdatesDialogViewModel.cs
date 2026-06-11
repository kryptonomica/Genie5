using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using Avalonia.Threading;
using Genie.App.Services;
using Genie.Core.Mapper;
using Genie.Core.Plugins;
using Genie.Core.Update;
using Genie.Core.Update.Sources;
using Genie.Core.Update.Updaters;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Genie.App.ViewModels;

/// <summary>
/// Backs the Updates dialog. Three tabs:
///
///   - <b>Core</b>: placeholder for Phase 4. Shows the installed Genie.App
///     assembly version + the selected channel; no live update path yet
///     (Velopack integration lands in Phase 4).
///   - <b>Maps</b>: one <see cref="UpdateFeedRow"/> per github-contents
///     feed. Per-row Check uses <see cref="MapsUpdater.CheckAsync"/>,
///     Update calls <see cref="MapsUpdater.ApplyAsync"/> on a single-source
///     updater so each row operates independently — same code path the
///     File → Update Maps menu uses.
///   - <b>Plugins</b>: one row per github-releases feed. Per-row Check
///     and Update use <see cref="PluginUpdater"/> wired against the live
///     <see cref="PluginManager"/> so installs hot-swap without restart.
///
/// All persistence goes through <see cref="FeedConfigStore"/> at
/// <c>{ConfigDir}/update-feeds.json</c>.
/// </summary>
public sealed class UpdatesDialogViewModel : ReactiveObject
{
    private readonly FeedConfigStore     _store;
    private readonly string              _mapsDir;
    private readonly string              _pluginsDir;
    private readonly MapZoneRepository   _zoneRepo;
    private readonly PluginManager?      _pluginManager;
    private          FeedConfig          _config;

    // ── Core tab (Velopack-backed) ─────────────────────────────────────────

    /// <summary>Friendly version string ("5.0.0-alpha.3.2") read via
    /// <see cref="Genie.Core.GenieCore.HostVersionString"/> — sourced from the
    /// csproj's <c>&lt;InformationalVersion&gt;</c>, NOT the assembly version
    /// (which is pinned at <c>5.0.0.0</c> for strong-name binding stability
    /// across point releases). Distinct from
    /// <see cref="CoreAppUpdater.CurrentVersion"/> which reflects Velopack's
    /// install manifest (only meaningful when launched from a vpk install).</summary>
    public string CoreInstalledVersion { get; }

    /// <summary>Latest release available on the selected channel, or a state message
    /// ("Up to date", "(dev build)", an error). Drives the Core tab's status line.</summary>
    [Reactive] public string CoreLatestVersion { get; private set; } = "(not checked yet)";

    /// <summary>True after a successful Check that found a newer release — gates the Update button.</summary>
    [Reactive] public bool   CoreUpdateAvailable { get; private set; }

    /// <summary>True while a Check or Update is in flight on the Core tab.</summary>
    [Reactive] public bool   CoreIsBusy          { get; private set; }

    /// <summary>True when running from a Velopack install — when false, Update is disabled with a friendly message.</summary>
    [Reactive] public bool   CoreCanUpdate       { get; private set; }

    /// <summary>Bound to the channel ComboBox. Persisted on change to <see cref="CoreFeed.Channel"/>.</summary>
    [Reactive] public string CoreChannel { get; set; } = "beta";

    public IReadOnlyList<string> AvailableChannels { get; } = new[] { "stable", "beta" };

    public ReactiveCommand<Unit, Unit> CheckCoreCommand  { get; private set; } = null!;
    public ReactiveCommand<Unit, Unit> UpdateCoreCommand { get; private set; } = null!;

    /// <summary>
    ///   The Velopack platform channel resolved at construction time
    ///   (e.g. "linux", "osx", "osx-x64", or "(default / win)" on Windows).
    ///   Surfaces on the Core tab so the user / Jason can confirm at a
    ///   glance that a Mac/Linux install is reading the right RELEASES file.
    /// </summary>
    [Reactive] public string CorePlatformChannel { get; private set; } = "(default / win)";

    private CoreAppUpdater _coreUpdater = null!;

    // ── Maps + Plugins tabs ────────────────────────────────────────────────

    public ObservableCollection<UpdateFeedRow> MapsFeeds    { get; } = new();
    public ObservableCollection<UpdateFeedRow> PluginsFeeds { get; } = new();

    // ── Global / footer ────────────────────────────────────────────────────

    [Reactive] public string Status  { get; private set; } = "";
    [Reactive] public bool   IsBusy  { get; private set; }

    public ReactiveCommand<Unit, Unit>                       CheckAllCommand           { get; }
    public ReactiveCommand<Unit, Unit>                       AddMapsSourceCommand      { get; }
    public ReactiveCommand<Unit, Unit>                       AddPluginsSourceCommand   { get; }

    /// <summary>
    /// Code-behind handler shows the Add Source dialog. The bool tells the
    /// dialog whether to display the third-party-code acknowledgment
    /// (true = plugin source, false = map source).
    /// </summary>
    public Interaction<bool, FeedEntry?>                     ShowAddSourceDialog       { get; } = new();

    /// <summary>Designer ctor.</summary>
    public UpdatesDialogViewModel() : this(
        new FeedConfigStore(Path.GetTempPath()),
        Path.GetTempPath(),
        Path.GetTempPath(),
        new MapZoneRepository(),
        null) { }

    public UpdatesDialogViewModel(
        FeedConfigStore      store,
        string               mapsDir,
        string               pluginsDir,
        MapZoneRepository    zoneRepo,
        PluginManager?       pluginManager)
    {
        _store         = store;
        _mapsDir       = mapsDir;
        _pluginsDir    = pluginsDir;
        _zoneRepo      = zoneRepo;
        _pluginManager = pluginManager;

        _config = _store.Load();
        CoreChannel          = string.IsNullOrWhiteSpace(_config.Core.Channel) ? "beta" : _config.Core.Channel;
        CoreInstalledVersion = Genie.Core.GenieCore.HostVersionString;

        RebuildCoreUpdater();
        CoreLatestVersion = CoreCanUpdate
            ? "(not checked yet)"
            : "(dev build — install via Velopack-built installer to enable in-app updates)";

        // Channel change: persist immediately AND rebuild the CoreAppUpdater
        // since prerelease=true/false is baked into Velopack's GithubSource
        // at construction time, not switched on the fly.
        this.WhenAnyValue(x => x.CoreChannel)
            .Skip(1)
            .Subscribe(v =>
            {
                _config.Core.Channel = v;
                _store.Save(_config);
                RebuildCoreUpdater();
                CoreLatestVersion   = "(not checked yet)";
                CoreUpdateAvailable = false;
            });

        RefreshRows();

        var notBusy     = this.WhenAnyValue(x => x.IsBusy).Select(b => !b);
        var coreNotBusy = this.WhenAnyValue(x => x.CoreIsBusy).Select(b => !b);
        var canUpdate   = this.WhenAnyValue(
            x => x.CoreIsBusy, x => x.CoreUpdateAvailable, x => x.CoreCanUpdate,
            (busy, avail, can) => !busy && avail && can);

        CheckAllCommand         = ReactiveCommand.CreateFromTask(CheckAllAsync,    notBusy);
        AddMapsSourceCommand    = ReactiveCommand.CreateFromTask(AddMapsAsync,     notBusy);
        AddPluginsSourceCommand = ReactiveCommand.CreateFromTask(AddPluginsAsync,  notBusy);
        CheckCoreCommand        = ReactiveCommand.CreateFromTask(CheckCoreAsync,   coreNotBusy);
        UpdateCoreCommand       = ReactiveCommand.CreateFromTask(UpdateCoreAsync,  canUpdate);
    }

    private void RebuildCoreUpdater()
    {
        var url = $"https://github.com/{_config.Core.Owner}/{_config.Core.Repo}";
        _coreUpdater        = new CoreAppUpdater(url, _config.Core.Channel);
        CoreCanUpdate       = _coreUpdater.IsInstalled;
        CorePlatformChannel = _coreUpdater.PlatformChannel;
    }

    private async Task CheckCoreAsync()
    {
        CoreIsBusy = true;
        try
        {
            var r = await _coreUpdater.CheckAsync();
            CoreUpdateAvailable = r.UpdateAvailable;
            CoreLatestVersion   = r.UpdateAvailable
                ? $"{r.LatestVersion} (you have {_coreUpdater.CurrentVersion})"
                : r.LatestVersion;
            Status = r.UpdateAvailable
                ? $"Core update available: {r.LatestVersion}"
                : "Core is up to date.";
        }
        catch (Exception ex) { Status = $"Core check failed: {ex.Message}"; }
        finally { CoreIsBusy = false; }
    }

    private async Task UpdateCoreAsync()
    {
        CoreIsBusy = true;
        try
        {
            var progress = new Progress<UpdateProgress>(p =>
                Dispatcher.UIThread.Post(() =>
                    Status = $"Core: {p.Item} — {p.Status}"));
            var r = await _coreUpdater.ApplyAsync(progress);
            Status = r.Summary;
            // On success Velopack should be relaunching the app — execution past
            // this point is rare. We leave the flag set so the UI doesn't allow
            // another download race in the brief window before exit.
        }
        catch (Exception ex) { Status = $"Core update failed: {ex.Message}"; }
        finally { CoreIsBusy = false; }
    }

    // ── Row construction ───────────────────────────────────────────────────

    private void RefreshRows()
    {
        MapsFeeds.Clear();
        foreach (var f in _config.Maps)
            MapsFeeds.Add(BuildMapsRow(f));

        PluginsFeeds.Clear();
        foreach (var f in _config.Plugins)
            PluginsFeeds.Add(BuildPluginsRow(f));
    }

    private UpdateFeedRow BuildMapsRow(FeedEntry feed) =>
        new(feed,
            check:  (f, ct)         => CheckOneAsync(f,  Kind.Maps,    ct),
            apply:  (f, prog, ct)   => ApplyOneAsync(f,  Kind.Maps,    prog, ct),
            toggle: (f, on)         => ToggleFeed(f, on),
            remove: f               => RemoveFeed(f, Kind.Maps));

    private UpdateFeedRow BuildPluginsRow(FeedEntry feed) =>
        new(feed,
            check:  (f, ct)         => CheckOneAsync(f,  Kind.Plugins, ct),
            apply:  (f, prog, ct)   => ApplyOneAsync(f,  Kind.Plugins, prog, ct),
            toggle: (f, on)         => ToggleFeed(f, on),
            remove: f               => RemoveFeed(f, Kind.Plugins));

    // ── Per-feed Check / Apply (dispatched by Kind) ────────────────────────

    private enum Kind { Maps, Plugins }

    private async Task<string> CheckOneAsync(FeedEntry feed, Kind kind, CancellationToken ct)
    {
        try
        {
            IUpdater u = BuildUpdater(feed, kind);
            var r = await u.CheckAsync(ct);
            feed.LastChecked = DateTimeOffset.UtcNow;
            _store.Save(_config);
            return r.UpdateAvailable
                ? $"Update available: {u.CurrentVersion} → {r.LatestVersion}"
                : $"Up to date ({u.CurrentVersion}).";
        }
        catch (Exception ex)
        {
            return $"Check failed: {ex.Message}";
        }
    }

    private async Task<string> ApplyOneAsync(
        FeedEntry                  feed,
        Kind                       kind,
        IProgress<UpdateProgress>? progress,
        CancellationToken          ct)
    {
        try
        {
            IUpdater u = BuildUpdater(feed, kind);
            var r = await u.ApplyAsync(progress, ct);
            feed.LastChecked = DateTimeOffset.UtcNow;
            _store.Save(_config);
            return r.Summary;
        }
        catch (Exception ex)
        {
            return $"Update failed: {ex.Message}";
        }
    }

    private IUpdater BuildUpdater(FeedEntry feed, Kind kind)
    {
        if (kind == Kind.Maps)
        {
            // One single-source MapsUpdater per row. The CheckAsync uses the
            // existing source's blob-sha skip optimization, so re-checking
            // is cheap even with many feeds.
            return new MapsUpdater(
                _zoneRepo, _mapsDir,
                new[] { (IFileListSource)MakeFileListSource(feed) });
        }
        else
        {
            return new PluginUpdater(
                feed:       feed,
                source:     MakeReleaseSource(feed),
                pluginsDir: _pluginsDir,
                manager:    _pluginManager,
                channel:    _config.Core.Channel);
        }
    }

    private static IFileListSource MakeFileListSource(FeedEntry feed) =>
        feed.Kind.Equals("github-contents", StringComparison.OrdinalIgnoreCase)
            ? new GithubContentsSource(feed.Owner, feed.Repo, feed.Path, feed.Extension)
            : throw new NotSupportedException($"Maps source kind '{feed.Kind}' is not supported yet.");

    private static IReleaseSource MakeReleaseSource(FeedEntry feed) =>
        feed.Kind.Equals("github-releases", StringComparison.OrdinalIgnoreCase)
            ? new GithubReleasesSource(feed.Owner, feed.Repo)
            : throw new NotSupportedException($"Plugin source kind '{feed.Kind}' is not supported yet.");

    // ── Toggle / Remove ────────────────────────────────────────────────────

    private void ToggleFeed(FeedEntry feed, bool on)
    {
        feed.Enabled = on;
        _store.Save(_config);
        Status = $"'{feed.Name}' {(on ? "enabled" : "disabled")}.";
    }

    private void RemoveFeed(FeedEntry feed, Kind kind)
    {
        var list = kind == Kind.Maps ? _config.Maps : _config.Plugins;
        list.Remove(feed);
        _store.Save(_config);
        RefreshRows();
        Status = $"Removed '{feed.Name}'.";
    }

    // ── Add Source flow ────────────────────────────────────────────────────

    private async Task AddMapsAsync()
    {
        var entry = await ShowAddSourceDialog.Handle(false);
        if (entry is null) return;
        // Re-tag the parsed entry as a maps-kind contents source. The
        // PluginSourceParser produces a github-releases plugin entry by
        // default — flip to github-contents + .xml filter for maps.
        entry.Kind         = "github-contents";
        entry.Extension    = ".xml";
        entry.AssetPattern = "";
        DedupeAdd(_config.Maps, entry);
        _store.Save(_config);
        RefreshRows();
        Status = $"Added Maps source '{entry.Name}' ({entry.Owner}/{entry.Repo}).";
    }

    private async Task AddPluginsAsync()
    {
        var entry = await ShowAddSourceDialog.Handle(true);
        if (entry is null) return;
        DedupeAdd(_config.Plugins, entry);
        _store.Save(_config);
        RefreshRows();
        Status = $"Added plugin source '{entry.Name}' ({entry.Owner}/{entry.Repo}). Use Update to install.";
    }

    private static void DedupeAdd(List<FeedEntry> list, FeedEntry entry)
    {
        var existing = list.FirstOrDefault(e =>
            e.Id.Equals(entry.Id, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) list.Remove(existing);
        list.Add(entry);
    }

    // ── Check All ──────────────────────────────────────────────────────────

    private async Task CheckAllAsync()
    {
        IsBusy = true;
        try
        {
            var rows = MapsFeeds.Concat(PluginsFeeds).Where(r => r.Enabled).ToList();
            int   done   = 0;
            foreach (var row in rows)
            {
                done++;
                Status = $"Checking {done}/{rows.Count}: {row.Name}…";
                await Dispatcher.UIThread.InvokeAsync(() => row.CheckCommand.Execute().Subscribe());
            }
            Status = $"Checked {rows.Count} enabled source(s).";
        }
        finally { IsBusy = false; }
    }
}
