using System.Reactive;
using System.Reactive.Linq;
using Avalonia.Threading;
using Genie.Core.Update;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Genie.App.ViewModels;

/// <summary>
/// One row in the Updates dialog's Maps or Plugins tab. Wraps a
/// <see cref="FeedEntry"/> with per-row UI state (status text, busy flag,
/// enabled toggle) and the three actions the user can take on it from the
/// row's own buttons: Check (cheap availability poll), Update (download +
/// apply), and Remove (delete from feed config).
///
/// The actual update / check / persistence logic lives in the parent
/// <see cref="UpdatesDialogViewModel"/>; this row receives delegates instead
/// of services so it stays free of feed-kind dispatch (github-releases vs.
/// github-contents) and config I/O.
/// </summary>
public sealed class UpdateFeedRow : ReactiveObject
{
    private readonly FeedEntry                                                _feed;
    private readonly Func<FeedEntry, CancellationToken, Task<string>>         _check;
    private readonly Func<FeedEntry, IProgress<UpdateProgress>?, CancellationToken, Task<string>> _apply;
    private readonly Action<FeedEntry, bool>                                  _toggle;
    private readonly Action<FeedEntry>                                        _remove;

    public string  Name         => _feed.Name;
    public string  Kind         => _feed.Kind;
    /// <summary>Owner/Repo for github-* sources, the URL for http-manifest.</summary>
    public string  Source       => string.IsNullOrEmpty(_feed.Owner)
                                       ? _feed.ManifestUrl
                                       : $"{_feed.Owner}/{_feed.Repo}";
    public string  AssetPattern => string.IsNullOrEmpty(_feed.AssetPattern)
                                       ? "(all files)"
                                       : _feed.AssetPattern;

    /// <summary>
    /// Persists immediately when the user toggles the row's checkbox — the
    /// setter calls into the toggle delegate which writes the config and
    /// updates the underlying FeedEntry.
    /// </summary>
    [Reactive] public bool   Enabled  { get; set; }

    [Reactive] public string Status   { get; private set; } = "";
    [Reactive] public bool   IsBusy   { get; private set; }

    public ReactiveCommand<Unit, Unit> CheckCommand  { get; }
    public ReactiveCommand<Unit, Unit> UpdateCommand { get; }
    public ReactiveCommand<Unit, Unit> RemoveCommand { get; }

    public UpdateFeedRow(
        FeedEntry                                                                feed,
        Func<FeedEntry, CancellationToken, Task<string>>                         check,
        Func<FeedEntry, IProgress<UpdateProgress>?, CancellationToken, Task<string>> apply,
        Action<FeedEntry, bool>                                                  toggle,
        Action<FeedEntry>                                                        remove)
    {
        _feed    = feed;
        _check   = check;
        _apply   = apply;
        _toggle  = toggle;
        _remove  = remove;
        Enabled  = feed.Enabled;

        // Toggle-on-change: persist whenever the checkbox flips. Skip the
        // initial value (just set above) by sub-after-construct via Skip(1).
        this.WhenAnyValue(x => x.Enabled)
            .Skip(1)
            .Subscribe(v => _toggle(_feed, v));

        var notBusy = this.WhenAnyValue(x => x.IsBusy).Select(b => !b);

        CheckCommand  = ReactiveCommand.CreateFromTask(RunCheck,  notBusy);
        UpdateCommand = ReactiveCommand.CreateFromTask(RunUpdate, notBusy);
        RemoveCommand = ReactiveCommand.Create(() => _remove(_feed), notBusy);
    }

    private async Task RunCheck()
    {
        IsBusy = true;
        try
        {
            Status = "Checking…";
            Status = await _check(_feed, CancellationToken.None);
        }
        catch (Exception ex) { Status = $"Error: {ex.Message}"; }
        finally { IsBusy = false; }
    }

    private async Task RunUpdate()
    {
        IsBusy = true;
        try
        {
            var progress = new Progress<UpdateProgress>(p =>
                Dispatcher.UIThread.Post(() =>
                    Status = $"[{p.Current}/{p.Total}] {p.Item} — {p.Status}"));

            Status = await _apply(_feed, progress, CancellationToken.None);
        }
        catch (Exception ex) { Status = $"Error: {ex.Message}"; }
        finally { IsBusy = false; }
    }
}
