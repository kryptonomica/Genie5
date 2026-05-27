using Avalonia.Controls;
using Avalonia.Interactivity;
using Genie.App.Controls;
using Genie.Core.Layout;

namespace Genie.App.Views;

/// <summary>
/// Per-window display settings editor. Lets the user customise title, font,
/// fg/bg colour, timestamp toggle, and "redirect if closed" routing for each
/// registered dockable. Ported from dylb0t/Genie5 with our top/bottom layout
/// + persistence callback shape.
/// </summary>
public partial class LayoutPanel : UserControl
{
    private WindowSettingsStore? _store;
    private WindowSettings?      _current;
    private Action?              _onChanged;

    /// <summary>Sentinel labels used in the IfClosed dropdown — mirror
    /// Genie 4 / Wrayth UCWindows semantics.</summary>
    private const string IfClosedDefaultLabel  = "(default)";
    private const string IfClosedDisabledLabel = "(disabled)";

    public LayoutPanel() => InitializeComponent();

    public void Initialize(WindowSettingsStore store, Action? onChanged = null)
    {
        _store     = store;
        _onChanged = onChanged;
        Refresh();
    }

    private void Refresh()
    {
        if (_store is null) return;

        WindowList.ItemsSource = _store.All.Values
            .Select(s => s.DisplayTitle)
            .ToList();

        // IfClosed dropdown: (default), (disabled), then every other window's title.
        var items = new List<string> { IfClosedDefaultLabel, IfClosedDisabledLabel };
        items.AddRange(_store.All.Values.Select(s => s.DisplayTitle));
        IfClosedBox.ItemsSource = items;

        if (_store.All.Count > 0) WindowList.SelectedIndex = 0;
    }

    private void OnWindowSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (_store is null || WindowList.SelectedIndex < 0) return;
        var id = _store.All.Keys.ElementAtOrDefault(WindowList.SelectedIndex);
        if (id is null) return;

        _current = _store.Get(id);
        LoadForm(_current);
        StatusText.Text = string.Empty;
    }

    private void LoadForm(WindowSettings s)
    {
        TitleBox.Text       = s.DisplayTitle;
        FontFamilyBox.Text  = s.FontFamily;
        FontSizeBox.Text    = s.FontSize.ToString("G");
        ColorPickerHelpers.LoadColor(FgColorPicker, FgDefaultCheck, s.Foreground, "Default");
        ColorPickerHelpers.LoadColor(BgColorPicker, BgNoneCheck,    s.Background, "");
        TimestampCheck.IsChecked = s.Timestamp;
        IfClosedBox.SelectedItem = IfClosedToLabel(s.IfClosed);
    }

    private void OnApply(object? sender, RoutedEventArgs e)
    {
        if (_current is null) { StatusText.Text = "Select a window first."; return; }

        var title      = TitleBox.Text?.Trim()      ?? string.Empty;
        var fontFamily = FontFamilyBox.Text?.Trim() ?? string.Empty;
        var fontSzText = FontSizeBox.Text?.Trim()   ?? string.Empty;

        if (!double.TryParse(fontSzText, out var fontSize) || fontSize < 6 || fontSize > 72)
        {
            StatusText.Text = "Font size must be a number between 6 and 72.";
            return;
        }

        _current.DisplayTitle = string.IsNullOrEmpty(title)      ? _current.DefaultTitle : title;
        _current.FontFamily   = string.IsNullOrEmpty(fontFamily) ? _current.FontFamily   : fontFamily;
        _current.FontSize     = fontSize;
        _current.Foreground   = ColorPickerHelpers.ReadColor(FgColorPicker, FgDefaultCheck, "Default");
        _current.Background   = ColorPickerHelpers.ReadColor(BgColorPicker, BgNoneCheck,    "");
        _current.Timestamp    = TimestampCheck.IsChecked == true;
        _current.IfClosed     = LabelToIfClosed(IfClosedBox.SelectedItem as string);
        _current.NotifyChanged();

        Refresh();   // Window list might reflect a renamed title
        _onChanged?.Invoke();
        StatusText.Text = "Applied.";
    }

    private void OnReset(object? sender, RoutedEventArgs e)
    {
        if (_current is null || _store is null) return;

        // Reset to the registration-time defaults. Register() returns a fresh
        // template; we copy its fields into the live instance so anyone subscribed
        // to `_current.Changed` sees the update.
        var fresh = _store.Register(_current.Id, _current.DefaultTitle);
        _current.DisplayTitle = _current.DefaultTitle;
        _current.FontFamily   = fresh.FontFamily;
        _current.FontSize     = fresh.FontSize;
        _current.Foreground   = fresh.Foreground;
        _current.Background   = fresh.Background;
        _current.Timestamp    = fresh.Timestamp;
        _current.IfClosed     = fresh.IfClosed;
        _current.NotifyChanged();

        LoadForm(_current);
        Refresh();
        _onChanged?.Invoke();
        StatusText.Text = "Reset to defaults.";
    }

    // ── IfClosed value / label mapping ───────────────────────────────────────

    private string IfClosedToLabel(string? raw)
    {
        if (raw == null) return IfClosedDefaultLabel;
        if (raw.Length == 0) return IfClosedDisabledLabel;
        if (_store != null && _store.All.TryGetValue(raw, out var matched))
            return matched.DisplayTitle;
        return raw;
    }

    private string? LabelToIfClosed(string? label)
    {
        if (label == null || label == IfClosedDefaultLabel) return null;
        if (label == IfClosedDisabledLabel) return string.Empty;
        if (_store != null)
        {
            var hit = _store.All.Values.FirstOrDefault(s => s.DisplayTitle == label);
            if (hit != null) return hit.Id;
        }
        return label;
    }
}
