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
/// <c>Clear</c> / <c>Time Stamp</c> / <c>Name List Only</c> / <c>Close Window</c>.
/// One instance per dockable, built by <see cref="GenieDockFactory"/> with only
/// the actions that apply to that window type; each menu item hides itself when
/// its command / capability is absent (so a Vitals window shows just "Close
/// Window", while a stream window shows all four).
/// </summary>
public sealed class WindowMenuModel : ReactiveObject
{
    private bool _isTimestampOn;
    private bool _isNameListOnlyOn;
    private readonly Action<bool>? _onTimestampToggled;
    private readonly Action<bool>? _onNameListOnlyToggled;

    public WindowMenuModel(
        ICommand?     clear                 = null,
        ICommand?     close                 = null,
        bool          timestampOn           = false,
        Action<bool>? onTimestampToggled    = null,
        bool          nameListOnlyOn        = false,
        Action<bool>? onNameListOnlyToggled = null)
    {
        ClearCommand           = clear;
        CloseCommand           = close;
        _isTimestampOn         = timestampOn;
        _onTimestampToggled    = onTimestampToggled;
        _isNameListOnlyOn      = nameListOnlyOn;
        _onNameListOnlyToggled = onNameListOnlyToggled;
    }

    public ICommand? ClearCommand { get; }
    public ICommand? CloseCommand { get; }

    // Capability flags drive each MenuItem's IsVisible — a window only shows the
    // items it actually supports. Timestamp / Name List Only are "supported" when
    // a toggle handler was supplied.
    public bool ShowClear        => ClearCommand is not null;
    public bool ShowClose        => CloseCommand is not null;
    public bool ShowTimestamp    => _onTimestampToggled    is not null;
    public bool ShowNameListOnly => _onNameListOnlyToggled is not null;

    /// <summary>Render the separator above "Close Window" only when Close
    /// coexists with at least one item above it (so a Close-only menu has no
    /// dangling leading separator).</summary>
    public bool ShowCloseSeparator =>
        ShowClose && (ShowClear || ShowTimestamp || ShowNameListOnly);

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

    /// <summary>Mirror an external Time Stamp change (e.g. the Layout tab) into
    /// the checkmark without re-invoking the toggle handler.</summary>
    public void SyncTimestamp(bool value) =>
        this.RaiseAndSetIfChanged(ref _isTimestampOn, value, nameof(IsTimestampOn));

    /// <summary>Mirror an external Name List Only change into the checkmark.</summary>
    public void SyncNameListOnly(bool value) =>
        this.RaiseAndSetIfChanged(ref _isNameListOnlyOn, value, nameof(IsNameListOnlyOn));
}
