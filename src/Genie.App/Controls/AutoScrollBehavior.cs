using System.Collections;
using System.Collections.Specialized;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Genie.App.Controls;

/// <summary>
/// Attached behaviour that keeps a ScrollViewer scrolled to the newest item
/// unless the user has manually scrolled up.
///
/// Usage (AXAML):
///   &lt;ScrollViewer x:Name="sv"
///                 controls:AutoScrollBehavior.ItemsSource="{Binding MyList}"&gt;
///
/// After setting ItemsSource, the ScrollViewer's Tag property is populated
/// with an AutoScrollState object. Bind to it for the overlay button:
///   &lt;Button IsVisible="{Binding #sv.Tag.IsScrolledUp}"
///           Command="{Binding #sv.Tag.JumpToBottomCommand}"/&gt;
/// </summary>
public static class AutoScrollBehavior
{
    public static readonly AttachedProperty<IEnumerable?> ItemsSourceProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, IEnumerable?>(
            "ItemsSource", typeof(AutoScrollBehavior));

    /// <summary>When true, the ScrollViewer stops auto-following new items —
    /// the "Pause Scrolling" window-menu toggle. Bound to a reactive flag on
    /// the dockable; turning it off snaps back to the newest line.</summary>
    public static readonly AttachedProperty<bool> PausedProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, bool>(
            "Paused", typeof(AutoScrollBehavior));

    static AutoScrollBehavior()
    {
        ItemsSourceProperty.Changed.AddClassHandler<ScrollViewer>(OnItemsSourceChanged);
        PausedProperty.Changed.AddClassHandler<ScrollViewer>(OnPausedChanged);
    }

    public static IEnumerable? GetItemsSource(AvaloniaObject o)         => o.GetValue(ItemsSourceProperty);
    public static void         SetItemsSource(AvaloniaObject o, IEnumerable? v) => o.SetValue(ItemsSourceProperty, v);

    public static bool GetPaused(AvaloniaObject o)         => o.GetValue(PausedProperty);
    public static void SetPaused(AvaloniaObject o, bool v) => o.SetValue(PausedProperty, v);

    private static void OnPausedChanged(ScrollViewer sv, AvaloniaPropertyChangedEventArgs e)
    {
        // The state object is created by the ItemsSource handler; if Paused is
        // set before ItemsSource (binding order isn't guaranteed), make one now
        // so the flag isn't lost.
        if (sv.Tag is not AutoScrollState state)
        {
            state  = new AutoScrollState(sv);
            sv.Tag = state;
        }
        state.Paused = e.NewValue is true;
    }

    private static void OnItemsSourceChanged(
        ScrollViewer sv, AvaloniaPropertyChangedEventArgs e)
    {
        // Create (or reuse) a state object and expose it through Tag so the
        // host template can bind IsScrolledUp / JumpToBottomCommand without
        // needing the ugly attached-property-in-path syntax.
        if (sv.Tag is not AutoScrollState state)
        {
            state  = new AutoScrollState(sv);
            sv.Tag = state;
        }

        if (e.OldValue is INotifyCollectionChanged old)
            old.CollectionChanged -= state.OnCollectionChanged;

        if (e.NewValue is INotifyCollectionChanged @new)
        {
            @new.CollectionChanged += state.OnCollectionChanged;
            Dispatcher.UIThread.Post(state.ScrollToBottom, DispatcherPriority.Loaded);
        }
    }
}

/// <summary>
/// Per-ScrollViewer state exposed via <see cref="ScrollViewer.Tag"/>.
/// Bind the overlay button to its <see cref="IsScrolledUp"/> and
/// <see cref="JumpToBottomCommand"/> properties.
/// </summary>
public sealed class AutoScrollState : ReactiveObject
{
    private readonly ScrollViewer _sv;
    private bool _atBottom = true;
    private bool _paused;

    [Reactive] public bool IsScrolledUp { get; private set; }

    /// <summary>"Pause Scrolling" — when true, new items no longer drag the view
    /// to the bottom; the user reads the frozen scrollback while the buffer
    /// keeps filling underneath. Flipping it back to false resumes auto-follow
    /// and jumps to the newest line.</summary>
    public bool Paused
    {
        get => _paused;
        set
        {
            if (_paused == value) return;
            _paused = value;
            // Resuming: catch up to whatever arrived while paused.
            if (!value) JumpToBottom();
        }
    }

    public ICommand JumpToBottomCommand { get; }

    internal AutoScrollState(ScrollViewer sv)
    {
        _sv = sv;
        _sv.ScrollChanged += OnScrollChanged;
        JumpToBottomCommand = ReactiveCommand.Create(JumpToBottom);
    }

    // ── Collection tracking ────────────────────────────────────────────────

    internal void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_paused) return;   // frozen: let the buffer grow without following it
        if (e.Action == NotifyCollectionChangedAction.Add && _atBottom)
            Dispatcher.UIThread.Post(ScrollToBottom, DispatcherPriority.Loaded);
    }

    // ── Scroll tracking ────────────────────────────────────────────────────

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        var max = _sv.Extent.Height - _sv.Viewport.Height;
        _atBottom    = max <= 0 || _sv.Offset.Y >= max - 10;
        IsScrolledUp = !_atBottom;
    }

    // ── Scroll helpers ─────────────────────────────────────────────────────

    public void ScrollToBottom()
    {
        // ScrollToEnd() uses the ScrollViewer's own up-to-date extent, so it
        // lands on the last line even when extent/viewport are mid-update
        // (right after a line is added, or after an MDI relayout) — more
        // robust than computing Offset from Extent - Viewport.
        _sv.ScrollToEnd();
    }

    private void JumpToBottom()
    {
        _atBottom    = true;
        IsScrolledUp = false;
        Dispatcher.UIThread.Post(ScrollToBottom, DispatcherPriority.Loaded);
    }
}
