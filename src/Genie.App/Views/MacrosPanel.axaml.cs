using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Genie.App.Controls;
using Genie.Core.Import;
using Genie.Core.Macros;

namespace Genie.App.Views;

/// <summary>
/// Macros editor — keyboard-shortcut → action mapping. Key strings follow the
/// Avalonia gesture syntax (e.g. <c>F1</c>, <c>Control+F2</c>, <c>Alt+NumPad5</c>).
/// </summary>
public partial class MacrosPanel : UserControl
{
    public sealed record MacroRow(string Key, string Action);

    private MacroEngine? _engine;
    private Action?      _onChanged;

    public MacrosPanel() => InitializeComponent();

    public void Initialize(MacroEngine engine, Action? onChanged = null)
    {
        _engine    = engine;
        _onChanged = onChanged;
        Refresh();
    }

    private void Refresh()
    {
        if (_engine is null) return;
        var keep = (ItemsList.SelectedItem as MacroRow)?.Key;
        ItemsList.ItemsSource = _engine.Rules
            .Select(r => new MacroRow(r.Key, r.Action))
            .ToList();
        if (keep is not null)
            ItemsList.SelectedItem = ((IEnumerable<MacroRow>)ItemsList.ItemsSource)
                .FirstOrDefault(r => r.Key == keep);
    }

    private MacroRule? SelectedRule()
    {
        if (_engine is null || ItemsList.SelectedItem is not MacroRow row) return null;
        return _engine.Rules.FirstOrDefault(r => r.Key == row.Key);
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var rule = SelectedRule();
        if (rule is null) return;
        KeyBox.Text     = rule.Key;
        ActionBox.Text  = rule.Action;
        StatusText.Text = string.Empty;
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        if (_engine is null) return;
        var key    = KeyBox.Text?.Trim() ?? string.Empty;
        var action = ActionBox.Text ?? string.Empty;
        if (string.IsNullOrEmpty(key)) { StatusText.Text = "Key is required."; return; }

        _engine.Add(key, action);
        Refresh();
        _onChanged?.Invoke();
        StatusText.Text = $"Saved '{key}'.";
    }

    private void OnDelete(object? sender, RoutedEventArgs e)
    {
        if (_engine is null) return;
        var rule = SelectedRule();
        if (rule is null) { StatusText.Text = "Select a macro to delete."; return; }
        _engine.Remove(rule.Key);
        ClearForm();
        Refresh();
        _onChanged?.Invoke();
        StatusText.Text = $"Deleted '{rule.Key}'.";
    }

    private void OnAdd  (object? sender, RoutedEventArgs e) => ClearForm();
    private void OnClear(object? sender, RoutedEventArgs e) => ClearForm();

    /// <summary>
    /// Capture the pressed key combo into <see cref="KeyBox"/>. The field
    /// is marked read-only on the XAML side so users can't type — focusing
    /// it and pressing the desired combo is the canonical entry path. The
    /// event is always marked handled so the window-level macro firer does
    /// NOT execute whatever macro is currently bound to that key.
    /// </summary>
    private void OnKeyBoxKeyDown(object? sender, KeyEventArgs e)
    {
        var key = MacroKeyConverter.ToMacroKey(e.Key, e.KeyModifiers);
        if (key is not null)
        {
            KeyBox.Text = key;
            StatusText.Text = $"Captured: {key}";
        }
        // Even when ToMacroKey returns null (modifier-only press), we still
        // mark handled so a stray F-key never fires the existing macro while
        // the user is editing.
        e.Handled = true;
    }

    private async void OnImport(object? sender, RoutedEventArgs e)
    {
        if (_engine is null) return;
        var parent = this.GetVisualRoot() as Window;
        if (parent?.StorageProvider is not { } sp) return;

        var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title         = "Import Macros",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Macro files") { Patterns = ["*.cfg", "*.txt"] }
            }
        });
        if (files is null || files.Count == 0) return;

        var path = files[0].TryGetLocalPath();
        if (string.IsNullOrEmpty(path)) return;

        var result = Genie4Importer.ImportMacros(path, _engine, ImportMode.Merge);
        Refresh();
        _onChanged?.Invoke();
        StatusText.Text = result.Skipped > 0
            ? $"Imported {result.Imported} macro(s), skipped {result.Skipped}."
            : $"Imported {result.Imported} macro(s).";
    }

    private void ClearForm()
    {
        ItemsList.SelectedItem = null;
        KeyBox.Text            = string.Empty;
        ActionBox.Text         = string.Empty;
        StatusText.Text        = string.Empty;
    }
}
