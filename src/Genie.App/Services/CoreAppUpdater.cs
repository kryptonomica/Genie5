using Genie.Core.Update;
using Velopack;
using Velopack.Sources;

namespace Genie.App.Services;

/// <summary>
/// <see cref="IUpdater"/> over Velopack's <see cref="UpdateManager"/>. Lives
/// in <c>Genie.App</c> (not <c>Genie.Core</c>) so the Velopack platform
/// dependency stays out of the pure-domain core assembly.
///
/// <para><b>Install state.</b> <see cref="UpdateManager.IsInstalled"/> is
/// only true when Genie is launched from a Velopack-built install (the
/// <c>vpk pack</c> + installer path). When the user is running from
/// <c>dotnet run</c> or a raw <c>publish/</c> directory, IsInstalled is
/// false and both Check and Apply short-circuit with a friendly diagnostic
/// instead of throwing — so the Updates dialog stays useful in dev too.</para>
///
/// <para><b>Channels.</b> Velopack's GitHubSource takes a <c>prerelease</c>
/// flag: <c>"beta"</c> in our feed config maps to <c>prerelease: true</c>
/// (include releases marked prerelease on GitHub); <c>"stable"</c> maps to
/// <c>prerelease: false</c> (skip them). Switching channels rebuilds the
/// source — cheap, no state to migrate.</para>
///
/// <para><b>Restart pivot.</b> <see cref="UpdateManager.ApplyUpdatesAndRestart"/>
/// is fire-and-forget: it spawns Velopack's helper, exits the running
/// process, and relaunches at the new version. The Apply call therefore
/// never observably "returns" in success — the process is gone by then.
/// We report success in the result for paper completeness; the caller
/// should expect the app to vanish shortly after.</para>
/// </summary>
public sealed class CoreAppUpdater : IUpdater
{
    private readonly UpdateManager _mgr;
    private readonly string        _channel;
    private          UpdateInfo?   _pendingUpdate;

    public string Name => "Genie 5";

    /// <summary>
    ///   Running app version per Velopack's own bookkeeping (the installed
    ///   package's manifest, NOT the assembly file version). Falls back to
    ///   the assembly informational version when running uninstalled — which
    ///   is what the Updates dialog already displays on the Core tab.
    /// </summary>
    public string CurrentVersion
        => _mgr.CurrentVersion?.ToString()
           ?? System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString()
           ?? "(unknown)";

    /// <summary>True when launched from a Velopack-built install — gate for the Apply path.</summary>
    public bool IsInstalled => _mgr.IsInstalled;

    public CoreAppUpdater(string repoUrl, string channel = "stable")
    {
        _channel = string.IsNullOrWhiteSpace(channel) ? "stable" : channel;
        var prerelease = string.Equals(_channel, "beta", StringComparison.OrdinalIgnoreCase);
        var source     = new GithubSource(repoUrl, accessToken: null, prerelease: prerelease);
        _mgr           = new UpdateManager(source);
    }

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken ct = default)
    {
        if (!_mgr.IsInstalled)
        {
            return new UpdateCheckResult(
                UpdateAvailable: false,
                LatestVersion:   "(dev build — install via Velopack to enable in-app updates)",
                Notes:           null);
        }

        try
        {
            _pendingUpdate = await _mgr.CheckForUpdatesAsync();
            if (_pendingUpdate is null)
            {
                return new UpdateCheckResult(
                    UpdateAvailable: false,
                    LatestVersion:   CurrentVersion,
                    Notes:           "Up to date.");
            }

            var v = _pendingUpdate.TargetFullRelease.Version.ToString();
            return new UpdateCheckResult(
                UpdateAvailable: true,
                LatestVersion:   v,
                Notes:           $"Update available on the {_channel} channel.");
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult(
                UpdateAvailable: false,
                LatestVersion:   $"unreachable ({ex.Message})",
                Notes:           null);
        }
    }

    public async Task<UpdateApplyResult> ApplyAsync(
        IProgress<UpdateProgress>? progress = null,
        CancellationToken          ct       = default)
    {
        if (!_mgr.IsInstalled)
        {
            return new UpdateApplyResult(
                Succeeded: false,
                Summary:   "Cannot update — running from source (no Velopack install).",
                Errors:    new[] { "UpdateManager.IsInstalled is false." });
        }

        try
        {
            // CheckAsync may not have run if the caller hit Apply directly.
            _pendingUpdate ??= await _mgr.CheckForUpdatesAsync();
            if (_pendingUpdate is null)
            {
                return new UpdateApplyResult(
                    Succeeded: true,
                    Summary:   "Already up to date.",
                    Errors:    Array.Empty<string>());
            }

            // Velopack reports download progress as int 0–100 via Action<int>.
            // Bridge into the framework's UpdateProgress shape so the dialog
            // row displays a consistent format.
            void OnVpkProgress(int pct) =>
                progress?.Report(new UpdateProgress(pct, 100, "Downloading", $"{pct}%"));

            progress?.Report(new UpdateProgress(0, 100, "Downloading", "starting…"));
            await _mgr.DownloadUpdatesAsync(_pendingUpdate, OnVpkProgress, cancelToken: ct);

            progress?.Report(new UpdateProgress(100, 100, "Applying", "restarting…"));
            // Fire-and-forget — process exits inside this call after
            // launching the Velopack updater. The next two lines are only
            // reached if the relaunch hasn't kicked in yet (rare).
            _mgr.ApplyUpdatesAndRestart(_pendingUpdate);

            return new UpdateApplyResult(
                Succeeded: true,
                Summary:   $"Updating to {_pendingUpdate.TargetFullRelease.Version} — restarting.",
                Errors:    Array.Empty<string>());
        }
        catch (Exception ex)
        {
            return new UpdateApplyResult(
                Succeeded: false,
                Summary:   $"Update failed: {ex.Message}",
                Errors:    new[] { ex.Message });
        }
    }
}
