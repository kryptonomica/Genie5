using System.Collections;
using System.Collections.Specialized;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Threading;
using ReactiveUI;

namespace Genie.App.Controls;

/// <summary>
/// A ScrollViewer that wraps an ItemsControl and automatically scrolls to the
/// newest item unless the user has manually scrolled up.
///
/// Why ScrollViewer instead of ListBox: when placed inside a Dock content area,
/// a ScrollViewer receives a finite height from the ContentPresenter and renders
/// correctly. A ListBox passes infinite height through its measure chain, causing
/// the internal VirtualizingStackPanel (or even plain StackPanel) to produce a
/// zero-height viewport and no visible items.
///
/// Exposes IsScrolledUp + JumpToBottomCommand so the host can overlay a "↓ Bottom"
/// button.
/// </summary>
public class AutoScrollViewer : ScrollViewer
{
    private readonly ItemsControl _items = new();
    private bool _atBottom = true;

    // ── Bindable properties ────────────────────────────────────────────────

    public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty =
        AvaloniaProperty.Register<AutoScrollViewer, IEnumerable?>(nameof(ItemsSource));

    public static readonly StyledProperty<IDataTemplate?> ItemTemplateProperty =
        AvaloniaProperty.Register<AutoScrollViewer, IDataTemplate?>(nameof(ItemTemplate));

    public static readonly StyledProperty<bool> IsScrolledUpProperty =
        AvaloniaProperty.Register<AutoScrollViewer, bool>(nameof(IsScrolledUp));

    public IEnumerable? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public IDataTemplate? ItemTemplate
    {
        get => GetValue(ItemTemplateProperty);
        set => SetValue(ItemTemplateProperty, value);
    }

    public bool IsScrolledUp
    {
        get => GetValue(IsScrolledUpProperty);
        private set => SetValue(IsScrolledUpProperty, value);
    }

    public ICommand JumpToBottomCommand { get; }

    // ── Construction ───────────────────────────────────────────────────────

    public AutoScrollViewer()
    {
        Content = _items;
        // Disable horizontal scrolling so the ItemsControl is constrained to
        // the viewport width — without this, TextWrapping="Wrap" gets infinite
        // available width and renders each line as one very wide unclipped strip.
        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled;
        VerticalScrollBarVisibility   = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
        JumpToBottomCommand = ReactiveCommand.Create(JumpToBottom);
        ScrollChanged += OnScrollChanged;
    }

    // ── Property forwarding ────────────────────────────────────────────────

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ItemsSourceProperty)
        {
            if (change.OldValue is INotifyCollectionChanged old)
                old.CollectionChanged -= OnCollectionChanged;

            _items.ItemsSource = (IEnumerable?)change.NewValue;

            if (change.NewValue is INotifyCollectionChanged @new)
            {
                @new.CollectionChanged += OnCollectionChanged;
                Dispatcher.UIThread.Post(ScrollToBottom, DispatcherPriority.Loaded);
            }
        }
        else if (change.Property == ItemTemplateProperty)
        {
            _items.ItemTemplate = (IDataTemplate?)change.NewValue;
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
        var max = Extent.Height - Viewport.Height;
        _atBottom = max <= 0 || Offset.Y >= max - 10;
        IsScrolledUp = !_atBottom;
    }

    // ── Scroll helpers ─────────────────────────────────────────────────────

    private void ScrollToBottom()
    {
        var max = Math.Max(0, Extent.Height - Viewport.Height);
        Offset = Offset.WithY(max);
    }

    public void JumpToBottom()
    {
        _atBottom = true;
        IsScrolledUp = false;
        Dispatcher.UIThread.Post(ScrollToBottom, DispatcherPriority.Loaded);
    }
}
