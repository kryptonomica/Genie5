using System.Windows.Input;
using ReactiveUI;

namespace Genie.App.Docking;

/// <summary>
/// A dockable that exposes a window right-click menu. Implemented by every
/// Tool / Document so <see cref="GenieDockFactory"/> can attach a
/// <see cref="WindowMenuModel"/> uniformly, and the shared ContextMenu (in the
/// ToolControl theme + the game-window template) can bind to it.
/// </summary>
public interface IWindowMenuHost
{
    WindowMenuModel? WindowMenu { get; set; }
}

/// <summary>
/// Backs the per-window right-click context menu — Genie 4's window menu:
/// <c>Copy All</c> / <c>Clear</c> / <c>Time Stamp</c> / <c>Name List Only</c> /
/// <c>Pause Scrolling</c> / <c>Float</c> / <c>Close Window</c>.
/// One instance per dockable, built by <see cref="GenieDockFactory"/> with only
/// the actions that apply to that window type; each menu item hides itself when
/// its command / capability is absent (so a Vitals window shows just "Float" +
/// "Close Window", while a stream window shows everything).
/// </summary>
public sealed class WindowMenuModel : ReactiveObject
{
    private bool _isTimestampOn;
    private bool _isNameListOnlyOn;
    private bool _isScrollPaused;
    private bool _isFloating;
    private readonly Action<bool>? _onTimestampToggled;
    private readonly Action<bool>? _onNameListOnlyToggled;
    private readonly Action<bool>? _onScrollPauseToggled;
    private readonly Func<bool>?   _floatStateProbe;

    public WindowMenuModel(
        ICommand?     clear                 = null,
        ICommand?     close                 = null,
        bool          timestampOn           = false,
        Action<bool>? onTimestampToggled    = null,
        bool          nameListOnlyOn        = false,
        Action<bool>? onNameListOnlyToggled = null,
        ICommand?     copyAll               = null,
        bool          scrollPausedOn        = false,
        Action<bool>? onScrollPauseToggled  = null,
        Action?       onToggleFloat         = null,
        Func<bool>?   floatStateProbe       = null)
    {
        ClearCommand           = clear;
        CloseCommand           = close;
        CopyAllCommand         = copyAll;
        _isTimestampOn         = timestampOn;
        _onTimestampToggled    = onTimestampToggled;
        _isNameListOnlyOn      = nameListOnlyOn;
        _onNameListOnlyToggled = onNameListOnlyToggled;
        _isScrollPaused        = scrollPausedOn;
        _onScrollPauseToggled  = onScrollPauseToggled;
        _floatStateProbe       = floatStateProbe;

        if (onToggleFloat is not null)
            ToggleFloatCommand = ReactiveCommand.Create(() =>
            {
                onToggleFloat();
                RefreshFloatState();   // verb flips immediately after the action
            });

        // Seed the float verb from the live tree (almost always "Float" — the
        // window starts docked); RefreshFloatState keeps it honest on each open.
        RefreshFloatState();
    }

    public ICommand? ClearCommand       { get; }
    public ICommand? CloseCommand       { get; }
    public ICommand? CopyAllCommand     { get; }
    public ICommand? ToggleFloatCommand { get; }

    // Capability flags drive each MenuItem's IsVisible — a window only shows the
    // items it actually supports. Timestamp / Name List Only / Pause are
    // "supported" when their toggle handler was supplied; Float when a toggle
    // action was supplied.
    public bool ShowClear        => ClearCommand          is not null;
    public bool ShowClose        => CloseCommand          is not null;
    public bool ShowCopyAll      => CopyAllCommand        is not null;
    public bool ShowTimestamp    => _onTimestampToggled   is not null;
    public bool ShowNameListOnly => _onNameListOnlyToggled is not null;
    public bool ShowPauseScroll  => _onScrollPauseToggled is not null;
    public bool ShowFloat        => ToggleFloatCommand    is not null;

    /// <summary>Render the separator above "Close Window" only when Close
    /// coexists with at least one item above it (so a Close-only menu has no
    /// dangling leading separator).</summary>
    public bool ShowCloseSeparator =>
        ShowClose && (ShowCopyAll || ShowClear || ShowTimestamp
                      || ShowNameListOnly || ShowPauseScroll || ShowFloat);

    /// <summary>Time Stamp checkbox state. Set by the TwoWay menu binding —
    /// flipping it runs the toggle handler (which updates the window's
    /// <c>WindowSettings.Timestamp</c> + persists).</summary>
    public bool IsTimestampOn
    {
        get => _isTimestampOn;
        set
        {
            if (_isTimestampOn == value) return;
            this.RaiseAndSetIfChanged(ref _isTimestampOn, value);
            _onTimestampToggled?.Invoke(value);
        }
    }

    /// <summary>Name List Only checkbox state — see <see cref="IsTimestampOn"/>.</summary>
    public bool IsNameListOnlyOn
    {
        get => _isNameListOnlyOn;
        set
        {
            if (_isNameListOnlyOn == value) return;
            this.RaiseAndSetIfChanged(ref _isNameListOnlyOn, value);
            _onNameListOnlyToggled?.Invoke(value);
        }
    }

    /// <summary>Pause Scrolling checkbox state. When on, the window stops
    /// auto-following new lines (freezes the scroll position); turning it off
    /// snaps back to the newest line. Transient — not persisted, matching
    /// Genie 4 (a fresh session always starts following).</summary>
    public bool IsScrollPaused
    {
        get => _isScrollPaused;
        set
        {
            if (_isScrollPaused == value) return;
            this.RaiseAndSetIfChanged(ref _isScrollPaused, value);
            _onScrollPauseToggled?.Invoke(value);
        }
    }

    /// <summary>"Float" when the window is docked, "Re-dock" when it's already
    /// floating in its own top-level window. Refreshed by
    /// <see cref="RefreshFloatState"/> when the menu opens (the user can drag a
    /// window out / back without the menu's knowledge).</summary>
    public string FloatHeader => _isFloating ? "Re-dock" : "Float";

    /// <summary>Re-read the live float state from the dock tree and update
    /// <see cref="FloatHeader"/>. Called on menu-open (via
    /// <see cref="WindowMenuBehavior"/>) and right after a Float / Re-dock.</summary>
    public void RefreshFloatState()
    {
        if (_floatStateProbe is null) return;
        var floating = _floatStateProbe();
        if (_isFloating == floating) return;
        _isFloating = floating;
        this.RaisePropertyChanged(nameof(FloatHeader));
    }

    /// <summary>Mirror an external Time Stamp change (e.g. the Layout tab) into
    /// the checkmark without re-invoking the toggle handler.</summary>
    public void SyncTimestamp(bool value) =>
        this.RaiseAndSetIfChanged(ref _isTimestampOn, value, nameof(IsTimestampOn));

    /// <summary>Mirror an external Name List Only change into the checkmark.</summary>
    public void SyncNameListOnly(bool value) =>
        this.RaiseAndSetIfChanged(ref _isNameListOnlyOn, value, nameof(IsNameListOnlyOn));
}
