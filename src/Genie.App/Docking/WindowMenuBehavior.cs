using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Genie.App.Controls;

namespace Genie.App.Docking;

/// <summary>
/// Attached behaviour for the per-window right-click <see cref="ContextMenu"/>.
///
/// <para><b>RefreshOnOpen</b> — re-reads the host's live float state just before
/// the menu opens, so the "Float" / "Re-dock" verb is always correct (the user
/// can drag a window out of / back into the dock without the menu model ever
/// being told). It also records the menu's target control so the shared
/// <see cref="CopySelectionCommand"/> knows which window's selection to copy,
/// and refreshes that command's enabled state.</para>
///
/// <para>The ContextMenu inherits the DataContext of the control it's attached
/// to — the dockable (an <see cref="IWindowMenuHost"/>) — which is how the menu
/// items bind <c>WindowMenu.*</c>.</para>
/// </summary>
public static class WindowMenuBehavior
{
    public static readonly AttachedProperty<bool> RefreshOnOpenProperty =
        AvaloniaProperty.RegisterAttached<ContextMenu, bool>(
            "RefreshOnOpen", typeof(WindowMenuBehavior));

    public static bool GetRefreshOnOpen(AvaloniaObject o) => o.GetValue(RefreshOnOpenProperty);
    public static void SetRefreshOnOpen(AvaloniaObject o, bool v) => o.SetValue(RefreshOnOpenProperty, v);

    /// <summary>The control the currently-open window menu was invoked on (its
    /// PlacementTarget). The Copy command reads the live text selection from this
    /// control's subtree. Only one context menu is open at a time, so a single
    /// static slot is sufficient.</summary>
    private static Control? _currentTarget;

    private static readonly CopyCommand _copy = new();

    /// <summary>Shared "Copy" command for the window menu — copies the current
    /// highlighted selection of whichever window the menu was opened on. Bound
    /// from XAML via <c>{x:Static docking:WindowMenuBehavior.CopySelectionCommand}</c>.</summary>
    public static ICommand CopySelectionCommand => _copy;

    static WindowMenuBehavior()
    {
        RefreshOnOpenProperty.Changed.AddClassHandler<ContextMenu>((menu, e) =>
        {
            // Re-subscribe defensively so toggling the flag never double-hooks.
            menu.Opening -= OnOpening;
            if (e.NewValue is true)
                menu.Opening += OnOpening;
        });
    }

    private static void OnOpening(object? sender, CancelEventArgs e)
    {
        if (sender is not ContextMenu menu) return;

        _currentTarget = menu.PlacementTarget as Control;

        if (menu.DataContext is IWindowMenuHost host)
            host.WindowMenu?.RefreshFloatState();

        // Grey "Copy" out when there's nothing highlighted in this window.
        _copy.RaiseCanExecuteChanged();
    }

    // ── Copy current selection ─────────────────────────────────────────────

    private static string? CurrentSelection()
    {
        if (_currentTarget is null) return null;

        // 1) Game window: cross-line selection owned by the LineSelection
        //    behavior (its rendered per-line SelectionStart/End survive a
        //    right-click; the behavior holds the authoritative range).
        var list = _currentTarget.GetSelfAndVisualDescendants()
                                 .OfType<ItemsControl>()
                                 .FirstOrDefault(LineSelection.GetEnabled);
        if (list is not null && LineSelection.GetSelectedText(list) is { Length: > 0 } cross)
            return cross;

        // 2) Streams / other text feeds: the native per-block selection. A
        //    right-click inside the highlight keeps it (Avalonia collapses only
        //    when the click lands outside the selection).
        var block = _currentTarget.GetSelfAndVisualDescendants()
                                  .OfType<SelectableTextBlock>()
                                  .FirstOrDefault(b => b.SelectionStart != b.SelectionEnd);
        return block?.SelectedText;
    }

    private sealed class CopyCommand : ICommand
    {
        public event System.EventHandler? CanExecuteChanged;
        public void RaiseCanExecuteChanged() =>
            CanExecuteChanged?.Invoke(this, System.EventArgs.Empty);

        public bool CanExecute(object? parameter) =>
            !string.IsNullOrEmpty(CurrentSelection());

        public async void Execute(object? parameter)
        {
            var text = CurrentSelection();
            if (string.IsNullOrEmpty(text)) return;
            if (TopLevel.GetTopLevel(_currentTarget)?.Clipboard is { } cb)
                await cb.SetTextAsync(text);
        }
    }
}
