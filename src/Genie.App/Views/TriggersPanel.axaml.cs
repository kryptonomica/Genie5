using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Genie.Core.Import;
using Genie.Core.Triggers;

namespace Genie.App.Views;

/// <summary>
/// Trigger editor — code-behind + named controls (dylb0t pattern).
/// A trigger matches a regex against incoming game text and fires its action
/// (typically a command or script invocation) when the pattern matches.
/// </summary>
public partial class TriggersPanel : UserControl
{
    public sealed record TriggerRow(string EnabledGlyph, string Pattern, string Action, string ClassName);

    private TriggerEngineFinal? _engine;
    private Action?             _onChanged;

    public TriggersPanel() => InitializeComponent();

    public void Initialize(TriggerEngineFinal engine, Action? onChanged = null)
    {
        _engine    = engine;
        _onChanged = onChanged;
        Refresh();
    }

    private void Refresh()
    {
        if (_engine is null) return;
        var keep = (ItemsList.SelectedItem as TriggerRow)?.Pattern;
        ItemsList.ItemsSource = _engine.Triggers
            .Select(t => new TriggerRow(t.IsEnabled ? "✓" : "✗", t.Pattern, t.Action, t.ClassName))
            .ToList();
        if (keep is not null)
            ItemsList.SelectedItem = ((IEnumerable<TriggerRow>)ItemsList.ItemsSource)
                .FirstOrDefault(r => r.Pattern == keep);
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_engine is null || ItemsList.SelectedItem is not TriggerRow row) return;
        var trigger = _engine.Triggers.FirstOrDefault(t => t.Pattern == row.Pattern);
        if (trigger is null) return;
        PatternBox.Text              = trigger.Pattern;
        ActionBox.Text               = trigger.Action;
        ClassBox.Text                = trigger.ClassName;
        CaseSensitiveCheck.IsChecked = trigger.CaseSensitive;
        EnabledCheck.IsChecked       = trigger.IsEnabled;
        StatusText.Text              = string.Empty;
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        if (_engine is null) return;
        var pattern       = PatternBox.Text?.Trim() ?? string.Empty;
        var action        = ActionBox.Text?.Trim() ?? string.Empty;
        var className     = ClassBox.Text?.Trim() ?? string.Empty;
        var caseSensitive = CaseSensitiveCheck.IsChecked == true;
        var enabled       = EnabledCheck.IsChecked == true;

        if (string.IsNullOrEmpty(pattern)) { StatusText.Text = "Pattern is required."; return; }

        try { _ = new Regex(pattern); }
        catch (RegexParseException ex) { StatusText.Text = $"Invalid regex: {ex.Message}"; return; }

        _engine.RemoveTrigger(pattern);
        _engine.AddTrigger(pattern, action, caseSensitive, enabled, className);
        Refresh();
        _onChanged?.Invoke();
        StatusText.Text = "Saved.";
    }

    private void OnDelete(object? sender, RoutedEventArgs e)
    {
        if (_engine is null) return;
        if (ItemsList.SelectedItem is not TriggerRow row) { StatusText.Text = "Select a trigger to delete."; return; }
        _engine.RemoveTrigger(row.Pattern);
        ClearForm();
        Refresh();
        _onChanged?.Invoke();
        StatusText.Text = "Deleted.";
    }

    private void OnToggle(object? sender, RoutedEventArgs e)
    {
        if (_engine is null) return;
        if (ItemsList.SelectedItem is not TriggerRow row) { StatusText.Text = "Select a trigger to toggle."; return; }
        var trigger = _engine.Triggers.FirstOrDefault(t => t.Pattern == row.Pattern);
        if (trigger is null) return;
        _engine.SetEnabled(trigger.Pattern, !trigger.IsEnabled);
        Refresh();
        _onChanged?.Invoke();
        StatusText.Text = $"Trigger {(trigger.IsEnabled ? "enabled" : "disabled")}.";
    }

    private void OnAdd  (object? sender, RoutedEventArgs e) => ClearForm();
    private void OnClear(object? sender, RoutedEventArgs e) => ClearForm();

    private async void OnImport(object? sender, RoutedEventArgs e)
    {
        if (_engine is null) return;
        var parent = this.GetVisualRoot() as Window;
        if (parent?.StorageProvider is not { } sp) return;

        var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title         = "Import Triggers",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Trigger files") { Patterns = ["*.cfg", "*.txt"] }
            }
        });
        if (files is null || files.Count == 0) return;

        var path = files[0].TryGetLocalPath();
        if (string.IsNullOrEmpty(path)) return;

        var result = Genie4Importer.ImportTriggers(path, _engine, ImportMode.Merge);
        Refresh();
        _onChanged?.Invoke();
        StatusText.Text = result.Skipped > 0
            ? $"Imported {result.Imported} trigger(s), skipped {result.Skipped}."
            : $"Imported {result.Imported} trigger(s).";
    }

    private void ClearForm()
    {
        ItemsList.SelectedItem       = null;
        PatternBox.Text              = string.Empty;
        ActionBox.Text               = string.Empty;
        ClassBox.Text                = string.Empty;
        CaseSensitiveCheck.IsChecked = false;
        EnabledCheck.IsChecked       = true;
        StatusText.Text              = string.Empty;
    }
}
