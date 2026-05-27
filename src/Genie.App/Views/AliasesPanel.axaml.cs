using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Genie.Core.Aliases;
using Genie.Core.Import;

namespace Genie.App.Views;

/// <summary>
/// Alias editor — code-behind + named controls (dylb0t pattern).
/// One alias = one Name → Expansion mapping. Typing the Name as a command
/// expands to Expansion before being sent to the game.
/// </summary>
public partial class AliasesPanel : UserControl
{
    public sealed record AliasRow(string EnabledGlyph, string Name, string Expansion, bool IsEnabled);

    private AliasEngine? _engine;
    private Action?      _onChanged;

    public AliasesPanel() => InitializeComponent();

    public void Initialize(AliasEngine engine, Action? onChanged = null)
    {
        _engine    = engine;
        _onChanged = onChanged;
        Refresh();
    }

    private void Refresh()
    {
        if (_engine is null) return;
        var keep = (ItemsList.SelectedItem as AliasRow)?.Name;
        ItemsList.ItemsSource = _engine.Aliases
            .Select(a => new AliasRow(a.IsEnabled ? "✓" : "✗", a.Name, a.Expansion, a.IsEnabled))
            .ToList();
        if (keep is not null)
            ItemsList.SelectedItem = ((IEnumerable<AliasRow>)ItemsList.ItemsSource)
                .FirstOrDefault(r => r.Name == keep);
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_engine is null || ItemsList.SelectedItem is not AliasRow row) return;
        var alias = _engine.Aliases.FirstOrDefault(a => a.Name == row.Name);
        if (alias is null) return;
        NameBox.Text           = alias.Name;
        ExpansionBox.Text      = alias.Expansion;
        EnabledCheck.IsChecked = alias.IsEnabled;
        StatusText.Text        = string.Empty;
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        if (_engine is null) return;
        var name      = NameBox.Text?.Trim() ?? string.Empty;
        var expansion = ExpansionBox.Text?.Trim() ?? string.Empty;
        var enabled   = EnabledCheck.IsChecked == true;

        if (string.IsNullOrEmpty(name)) { StatusText.Text = "Name is required."; return; }

        _engine.RemoveAlias(name);
        _engine.AddAlias(name, expansion, enabled);
        Refresh();
        _onChanged?.Invoke();
        StatusText.Text = $"Saved '{name}'.";
    }

    private void OnDelete(object? sender, RoutedEventArgs e)
    {
        if (_engine is null) return;
        if (ItemsList.SelectedItem is not AliasRow row) { StatusText.Text = "Select an alias to delete."; return; }
        _engine.RemoveAlias(row.Name);
        ClearForm();
        Refresh();
        _onChanged?.Invoke();
        StatusText.Text = $"Deleted '{row.Name}'.";
    }

    private void OnToggle(object? sender, RoutedEventArgs e)
    {
        if (_engine is null) return;
        if (ItemsList.SelectedItem is not AliasRow row) { StatusText.Text = "Select an alias to toggle."; return; }
        var alias = _engine.Aliases.FirstOrDefault(a => a.Name == row.Name);
        if (alias is null) return;
        _engine.SetEnabled(alias.Name, !alias.IsEnabled);
        Refresh();
        _onChanged?.Invoke();
        StatusText.Text = $"'{alias.Name}' {(alias.IsEnabled ? "enabled" : "disabled")}.";
    }

    private void OnAdd  (object? sender, RoutedEventArgs e) => ClearForm();
    private void OnClear(object? sender, RoutedEventArgs e) => ClearForm();

    private void ClearForm()
    {
        ItemsList.SelectedItem = null;
        NameBox.Text           = string.Empty;
        ExpansionBox.Text      = string.Empty;
        EnabledCheck.IsChecked = true;
        StatusText.Text        = string.Empty;
    }

    private async void OnImport(object? sender, RoutedEventArgs e)
    {
        if (_engine is null) return;
        var parent = this.GetVisualRoot() as Window;
        if (parent?.StorageProvider is not { } sp) return;

        var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title         = "Import Aliases",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Alias files") { Patterns = ["*.cfg", "*.txt"] }
            }
        });
        if (files is null || files.Count == 0) return;

        var path = files[0].TryGetLocalPath();
        if (string.IsNullOrEmpty(path)) return;

        var result = Genie4Importer.ImportAliases(path, _engine, ImportMode.Merge);
        Refresh();
        _onChanged?.Invoke();
        StatusText.Text = $"Imported {result.Imported} alias(es).";
    }
}
