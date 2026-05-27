using System.Collections.Specialized;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using ReactiveUI;

namespace Genie.App.Controls;

/// <summary>
/// ListBox that automatically scrolls to the newest item unless the user has
/// manually scrolled up. A bindable JumpToBottomCommand + IsScrolledUp property
/// let the host overlay a "jump to bottom" button.
/// </summary>
public class AutoScrollListBox : ListBox
{
    private ScrollViewer? _scroll;
    private bool _atBottom = true;

    // ── Bindable state ─────────────────────────────────────────────────────

    public static readonly StyledProperty<bool> IsScrolledUpProperty =
        AvaloniaProperty.Register<AutoScrollListBox, bool>(nameof(IsScrolledUp));

    public bool IsScrolledUp
    {
        get => GetValue(IsScrolledUpProperty);
        private set => SetValue(IsScrolledUpProperty, value);
    }

    public ICommand JumpToBottomCommand { get; }

    // ── Construction ───────────────────────────────────────────────────────

    public AutoScrollListBox()
    {
        JumpToBottomCommand = ReactiveCommand.Create(JumpToBottom);
    }

    // ── Template ───────────────────────────────────────────────────────────

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        if (_scroll is not null)
            _scroll.ScrollChanged -= OnScrollChanged;

        _scroll = e.NameScope.Find<ScrollViewer>("PART_ScrollViewer");

        if (_scroll is not null)
            _scroll.ScrollChanged += OnScrollChanged;
    }

    // ── ItemsSource tracking ───────────────────────────────────────────────

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property != ItemsSourceProperty) return;

        if (change.OldValue is INotifyCollectionChanged old)
            old.CollectionChanged -= OnCollectionChanged;

        if (change.NewValue is INotifyCollectionChanged @new)
        {
            @new.CollectionChanged += OnCollectionChanged;
            // Scroll to the end whenever a new source is bound
            Dispatcher.UIThread.Post(ScrollToBottom, DispatcherPriority.Loaded);
        }
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && _atBottom)
            Dispatcher.UIThread.Post(ScrollToBottom, DispatcherPriority.Loaded);
    }

    // ── Scroll tracking ────────────────────────────────────────────────────

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_scroll is null) return;
        var max = _scroll.Extent.Height - _scroll.Viewport.Height;
        _atBottom = max <= 0 || _scroll.Offset.Y >= max - 10;
        IsScrolledUp = !_atBottom;
    }

    // ── Scroll helpers ─────────────────────────────────────────────────────

    private void ScrollToBottom()
    {
        if (_scroll is null) return;
        var max = Math.Max(0, _scroll.Extent.Height - _scroll.Viewport.Height);
        _scroll.Offset = _scroll.Offset.WithY(max);
    }

    public void JumpToBottom()
    {
        _atBottom = true;
        IsScrolledUp = false;
        Dispatcher.UIThread.Post(ScrollToBottom, DispatcherPriority.Loaded);
    }
}
