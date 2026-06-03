using System.Reactive;
using System.Reactive.Subjects;
using Avalonia.Threading;
using Genie.Core;
using Genie.Core.Mapper;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Genie.App.Services;

/// <summary>
/// Drives step-by-step execution of an <see cref="AutoMapperEngine.FindPath"/>
/// result. The user clicks a room in the Mapper, the service produces an
/// <see cref="AutoWalkSession"/>, dispatches the first move via the
/// command pipeline, then advances the plan each time the engine reports
/// the player has entered the expected next room.
///
/// <para>
/// Cancellation surfaces (all of these terminate the session):
/// <list type="bullet">
///   <item>User presses Esc — wired in <c>MainWindow.KeyDown</c></item>
///   <item>User types any non-meta command — wired in <c>MainWindowViewModel.HandleCommandLine</c></item>
///   <item>App window has been unfocused past the configured window — only
///         when the user has opted in via
///         <c>GenieConfig.AutoWalkPauseOnUnfocus</c> (default OFF); the
///         service starts a timer on deactivate and fires <see cref="Pause"/>
///         when it expires</item>
///   <item>Connection drops — service subscribes to <c>core.StateStream</c>
///         and cancels on Disconnected</item>
///   <item>Player walks into an unexpected room (off-plan) — heuristic
///         cancel with diagnostic message</item>
/// </list>
/// </para>
///
/// <para>
/// Compliance posture: a click (or #goto) is direct user intent and the
/// walker is *responsive* to it (RT-gated stepping, not a command burst).
/// The unfocus auto-pause below is an OPTIONAL idle backstop, OFF by default
/// — DR policy asks for responsiveness, not window focus.
/// — The service refuses to send a step while paused.
/// — Never auto-resumes across disconnects: a fresh session must be
///   started from a fresh user click.
/// — The visible indicator + Esc cancel + typed-command-cancel are the
///   always-available user controls.
/// </para>
/// </summary>
public sealed class AutoWalkService : ReactiveObject
{
    private readonly GenieCore _core;
    private readonly AutoMapperEngine _mapEngine;

    /// <summary>
    /// Fallback unfocus-pause window (seconds) used only if config somehow
    /// reports a sub-minimum value. The live value comes from
    /// <see cref="GenieConfig.AutoWalkUnfocusSeconds"/>, and the whole
    /// behavior is gated OFF by default behind
    /// <see cref="GenieConfig.AutoWalkPauseOnUnfocus"/>.
    /// </summary>
    private const int DefaultUnfocusPauseSeconds = 60;

    /// <summary>
    /// The current session, or null when no walk is in progress. Bindable
    /// via Reactive — the Mapper status strip's IsVisible is gated on this
    /// being non-null.
    /// </summary>
    [Reactive] public AutoWalkSession? Current { get; private set; }

    /// <summary>
    /// True when at least one session has completed (Finished or Cancelled)
    /// in this app lifetime — used by the indicator to flash the final
    /// status briefly before clearing. Not used yet; reserved for v2.
    /// </summary>
    [Reactive] public string? LastStatusFlash { get; private set; }

    /// <summary>
    /// Convenience bool for XAML: `true` exactly when the current session
    /// is in <see cref="AutoWalkState.Paused"/>. Used to gate the Resume
    /// button's visibility on the indicator strip. Avoids needing an
    /// enum-equality value converter in AXAML.
    /// </summary>
    [Reactive] public bool IsCurrentPaused { get; private set; }

    // ── Cross-zone wait state ─────────────────────────────────────────
    // Surfaces as a progress-bar in the Mapper's auto-walk indicator
    // when the walker is mid-transit on a cross-zone hop with a known
    // wait window (boats, ferries, scheduled departures). The actual
    // arrival is driven by the destination-zone fingerprint match;
    // this countdown is just a UI hint so the user knows roughly how
    // long the wait should be — they can Esc to cancel any time.

    /// <summary>True while a cross-zone wait timer is active. Gates the
    /// progress bar's IsVisible in the Mapper indicator.</summary>
    [Reactive] public bool IsWaitingForCrossZone { get; private set; }

    /// <summary>Total wait window in seconds (average of WaitMin / Max).
    /// Used as ProgressBar.Maximum.</summary>
    [Reactive] public int CurrentWaitTotalSeconds { get; private set; }

    /// <summary>Seconds left in the expected wait window. Counts down
    /// from <see cref="CurrentWaitTotalSeconds"/> to 0; if 0 elapses
    /// before the destination zone arrives, the bar pegs at 0 but the
    /// walker keeps waiting for the room change.</summary>
    [Reactive] public int CurrentWaitSecondsLeft { get; private set; }

    /// <summary>Human-readable "4:23 left" — refreshed on every timer
    /// tick alongside <see cref="CurrentWaitSecondsLeft"/>. Made a
    /// concrete <c>[Reactive]</c> string rather than a computed getter
    /// so XAML bindings refresh without manual RaisePropertyChanged.</summary>
    [Reactive] public string CurrentWaitDisplay { get; private set; } = "";

    /// <summary>Short label describing what we're waiting for — e.g.
    /// "boarding boat". Reactive so the indicator text updates when
    /// the wait kicks off.</summary>
    [Reactive] public string CurrentWaitLabel { get; private set; } = "";

    private DispatcherTimer? _waitTimer;

    private readonly Subject<AutoWalkSession?> _sessionChanges = new();
    public IObservable<AutoWalkSession?> SessionChanges => _sessionChanges;

    private DispatcherTimer? _unfocusTimer;

    /// <summary>
    /// Cancel the current walk. Wired to the Cancel button in the indicator
    /// strip; also fires from Esc handler in MainWindow code-behind.
    /// </summary>
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }

    /// <summary>
    /// Resume from Paused. Wired to the Resume button (only visible when paused).
    /// </summary>
    public ReactiveCommand<Unit, Unit> ResumeCommand { get; }

    public AutoWalkService(GenieCore core, AutoMapperEngine mapEngine)
    {
        _core      = core;
        _mapEngine = mapEngine;

        CancelCommand = ReactiveCommand.Create(() => Cancel("user clicked Cancel"));
        ResumeCommand = ReactiveCommand.Create(() => Resume());

        // Step pump: each room change advances the walk if the player
        // arrived at the next room on the planned path. Posted to UI
        // thread because the engine event can fire from any thread the
        // parser observable is hopping through.
        _mapEngine.CurrentNodeChanged += () => Dispatcher.UIThread.Post(OnRoomChanged);
    }

    /// <summary>
    /// Plan a route from origin to destination and begin walking. Returns
    /// false if there's already an active walk, no path exists, or the
    /// destination is the current room.
    /// </summary>
    public bool Start(MapNode origin, MapNode destination)
    {
        if (Current is { State: AutoWalkState.Active or AutoWalkState.Paused })
        {
            // Already walking — don't start a second walk without explicit
            // cancel first. Genie 4's behaviour was to interrupt; we ask
            // for explicit cancel so the user is aware.
            return false;
        }

        if (origin.Id == destination.Id)
        {
            FlashStatus("Already here.");
            return false;
        }

        var moves = _mapEngine.FindPath(origin, destination);
        if (moves is null)
        {
            FlashStatus($"No path to '{destination.Title}'.");
            return false;
        }
        if (moves.Count == 0)
        {
            FlashStatus("Already here.");
            return false;
        }

        var session = new AutoWalkSession(origin, destination, moves);
        session.StatusMessage = $"Walking to {destination.Title} — {session.ProgressText} · Esc to cancel";
        Current = session;
        _sessionChanges.OnNext(session);
        IsCurrentPaused = false;

        // Kick off the first step. Subsequent steps fire from OnRoomChanged
        // when the player arrives at the expected next room.
        DispatchNextStep();
        return true;
    }

    /// <summary>
    /// Cancel the current walk with a reason. No-op if nothing's running.
    /// Always safe to call (e.g. from the disconnect handler).
    /// </summary>
    public void Cancel(string reason)
    {
        if (Current is null) return;
        Current.State         = AutoWalkState.Cancelled;
        Current.StatusMessage = $"Cancelled: {reason}";
        FlashStatus(Current.StatusMessage);
        _sessionChanges.OnNext(Current);
        Current = null;
        IsCurrentPaused = false;
        StopUnfocusTimer();
        StopWaitCountdown();
    }

    /// <summary>
    /// Pause — used by the unfocus timer. Distinct from Cancel because the
    /// user can Resume, picking up at the current step.
    /// </summary>
    public void Pause(string reason)
    {
        if (Current is null) return;
        if (Current.State != AutoWalkState.Active) return;
        Current.State         = AutoWalkState.Paused;
        Current.StatusMessage = $"Paused — {reason} (Resume / Cancel)";
        IsCurrentPaused = true;
        _sessionChanges.OnNext(Current);
        // Don't kill the wait countdown on pause — the boat doesn't
        // stop because the user alt-tabbed. The bar keeps ticking;
        // when they Resume the walker is just waiting for the room
        // change to fire whenever it actually does.
    }

    /// <summary>
    /// Resume from Paused. No-op if not paused. Fires the next step
    /// immediately — the user has signalled they're back at the keyboard.
    /// </summary>
    public void Resume()
    {
        if (Current is null) return;
        if (Current.State != AutoWalkState.Paused) return;
        Current.State         = AutoWalkState.Active;
        Current.StatusMessage = $"Walking to {Current.Destination.Title} — {Current.ProgressText} · Esc to cancel";
        IsCurrentPaused = false;
        _sessionChanges.OnNext(Current);
        DispatchNextStep();
    }

    /// <summary>
    /// Called when MainWindow loses focus. Opt-in only: does nothing unless
    /// the user has enabled <see cref="GenieConfig.AutoWalkPauseOnUnfocus"/>.
    /// When on, starts the configured unfocus timer; if it expires while the
    /// walk is still active, we Pause (the user clicks Resume to continue).
    /// </summary>
    public void OnWindowDeactivated()
    {
        if (Current is null || Current.State != AutoWalkState.Active) return;

        // Default OFF. DR's Scripting Policy is about being responsive to the
        // game, not about keeping the window focused — so this safeguard only
        // arms when the user has explicitly opted in.
        if (!_core.Config.AutoWalkPauseOnUnfocus) return;

        var seconds = Math.Max(DefaultUnfocusPauseSeconds, _core.Config.AutoWalkUnfocusSeconds);
        StopUnfocusTimer();
        _unfocusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
        _unfocusTimer.Tick += (_, _) =>
        {
            StopUnfocusTimer();
            Pause($"window unfocused {seconds}s");
        };
        _unfocusTimer.Start();
    }

    /// <summary>
    /// Called when MainWindow regains focus. Just stops the unfocus timer;
    /// doesn't auto-resume — the user has to click Resume.
    /// </summary>
    public void OnWindowActivated() => StopUnfocusTimer();

    private void StopUnfocusTimer()
    {
        _unfocusTimer?.Stop();
        _unfocusTimer = null;
    }

    private void OnRoomChanged()
    {
        if (Current is null || Current.State != AutoWalkState.Active) return;

        // Any room-change implicitly clears a pending cross-zone wait —
        // either we arrived at the target zone, or we landed somewhere
        // intra-zone that progresses the plan. Either way the countdown
        // is no longer meaningful.
        if (IsWaitingForCrossZone) StopWaitCountdown();

        // Did we arrive at the destination?
        if (_mapEngine.CurrentNode?.Id == Current.Destination.Id)
        {
            Current.StepsCompleted = Current.StepsTotal;
            Current.State          = AutoWalkState.Finished;
            Current.StatusMessage  = $"Arrived at {Current.Destination.Title}";
            FlashStatus(Current.StatusMessage);
            _sessionChanges.OnNext(Current);
            Current = null;
            StopUnfocusTimer();
            return;
        }

        // Otherwise advance the step counter and pump the next move. The
        // simple "match-by-position" approach trusts FindPath's ordering;
        // if the player got bounced into a different room we'll send a
        // wrong next move and cancel on the off-path heuristic.
        Current.StepsCompleted++;
        Current.StatusMessage = $"Walking to {Current.Destination.Title} — {Current.ProgressText} · Esc to cancel";
        _sessionChanges.OnNext(Current);

        if (Current.StepsCompleted >= Current.Plan.Count)
        {
            // Plan exhausted but we didn't match destination — must have
            // wandered off. Cancel rather than send arbitrary commands.
            Cancel("walked past destination");
            return;
        }

        DispatchNextStep();
    }

    private void DispatchNextStep()
    {
        if (Current is null || Current.State != AutoWalkState.Active) return;
        if (Current.StepsCompleted >= Current.Plan.Count) return;

        var step = Current.Plan[Current.StepsCompleted];

        // Cross-zone hop — surface the wait window to the user before
        // sending the transit verb. The engine doesn't enforce the wait
        // (we naturally wait for the destination zone to fingerprint-
        // match), but showing the expected window calibrates the user's
        // patience: a 5-10 minute boat wait shouldn't look like a hang.
        if (step.IsCrossZone && step.ExpectedWaitMinSeconds.HasValue)
        {
            var min = step.ExpectedWaitMinSeconds.Value;
            var max = step.ExpectedWaitMaxSeconds ?? min;
            var avg = (min + max) / 2;
            Current.StatusMessage =
                $"Walking to {Current.Destination.Title} — {Current.ProgressText} · "
                + $"{step.Description} (wait ~{min / 60}-{max / 60} min) · Esc to cancel";
            _sessionChanges.OnNext(Current);

            // Kick the countdown bar — visible in the Mapper indicator
            // strip until the destination zone fingerprints in. Pegs at
            // 0 if the wait expires before arrival (we don't know the
            // true ETA, the window is just a hint from the community
            // ZoneConnections.xml data).
            StartWaitCountdown(step.Description, avg);
        }

        // Send through ProcessInput so the same alias / trigger / command-
        // queue pipeline runs as if the user typed it. The CommandQueue
        // already handles RT gating, so we don't need to wait ourselves.
        _core.Commands.ProcessInput(step.Verb);
    }

    /// <summary>
    /// Start the boat-wait countdown for a cross-zone hop. Fires every
    /// second; updates <see cref="CurrentWaitSecondsLeft"/> and the
    /// derived display string. Stops automatically when the destination
    /// zone arrives (room-change handler) or the walk is cancelled.
    /// </summary>
    private void StartWaitCountdown(string label, int totalSeconds)
    {
        StopWaitCountdown();   // belt-and-braces in case a previous timer leaked

        CurrentWaitLabel        = label;
        CurrentWaitTotalSeconds = Math.Max(1, totalSeconds);
        CurrentWaitSecondsLeft  = CurrentWaitTotalSeconds;
        CurrentWaitDisplay      = FormatWait(CurrentWaitSecondsLeft);
        IsWaitingForCrossZone   = true;

        _waitTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _waitTimer.Tick += (_, _) =>
        {
            if (CurrentWaitSecondsLeft > 0)
                CurrentWaitSecondsLeft--;
            CurrentWaitDisplay = FormatWait(CurrentWaitSecondsLeft);
            // We let the timer continue past 0 — the bar stays empty
            // until the room change actually arrives. The user sees
            // "any moment now…" if the boat is running late.
        };
        _waitTimer.Start();
    }

    /// <summary>Stop the wait timer and clear the indicator. Idempotent;
    /// safe to call from any of the walk-terminating paths (room-
    /// change, cancel, pause, disconnect).</summary>
    private void StopWaitCountdown()
    {
        _waitTimer?.Stop();
        _waitTimer = null;
        IsWaitingForCrossZone   = false;
        CurrentWaitLabel        = "";
        CurrentWaitSecondsLeft  = 0;
        CurrentWaitTotalSeconds = 0;
        CurrentWaitDisplay      = "";
    }

    private static string FormatWait(int seconds)
    {
        if (seconds <= 0) return "any moment now…";
        var mins = seconds / 60;
        var secs = seconds % 60;
        return mins > 0 ? $"~{mins}:{secs:D2} left" : $"~{secs}s left";
    }

    private void FlashStatus(string text)
    {
        LastStatusFlash = text;
        // Auto-clear after 5 seconds so the flash doesn't linger.
        Avalonia.Threading.DispatcherTimer.RunOnce(() =>
        {
            if (LastStatusFlash == text) LastStatusFlash = null;
        }, TimeSpan.FromSeconds(5));
    }
}
