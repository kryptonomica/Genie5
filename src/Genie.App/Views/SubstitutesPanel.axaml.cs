using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Genie.Core.Import;
using Genie.Core.Substitutes;

namespace Genie.App.Views;

/// <summary>
/// Substitutes editor — replaces matched text in the game stream with a
/// different display value before rendering. Useful for shortening verbose
/// game messages or relabelling terms.
/// </summary>
public partial class SubstitutesPanel : UserControl
{
    public sealed record SubstituteRow(string EnabledGlyph, string Pattern, string Replacement, string ClassName);

    private SubstituteEngine? _engine;
    private Action?           _onChanged;

    public SubstitutesPanel() => InitializeComponent();

    public void Initialize(SubstituteEngine engine, Action? onChanged = null)
    {
        _engine    = engine;
        _onChanged = onChanged;
        Refresh();
    }

    private void Refresh()
    {
        if (_engine is null) return;
        var keep = (ItemsList.SelectedItem as SubstituteRow)?.Pattern;
        ItemsList.ItemsSource = _engine.Rules
            .Select(r => new SubstituteRow(r.IsEnabled ? "✓" : "✗", r.Pattern, r.Replacement, r.ClassName))
            .ToList();
        if (keep is not null)
            ItemsList.SelectedItem = ((IEnumerable<SubstituteRow>)ItemsList.ItemsSource)
                .FirstOrDefault(r => r.Pattern == keep);
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_engine is null || ItemsList.SelectedItem is not SubstituteRow row) return;
        var rule = _engine.Rules.FirstOrDefault(r => r.Pattern == row.Pattern);
        if (rule is null) return;
        PatternBox.Text              = rule.Pattern;
        ReplacementBox.Text          = rule.Replacement;
        ClassBox.Text                = rule.ClassName;
        CaseSensitiveCheck.IsChecked = rule.CaseSensitive;
        EnabledCheck.IsChecked       = rule.IsEnabled;
        StatusText.Text              = string.Empty;
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        if (_engine is null) return;
        var pattern       = PatternBox.Text?.Trim() ?? string.Empty;
        var replacement   = ReplacementBox.Text ?? string.Empty;
        var className     = ClassBox.Text?.Trim() ?? string.Empty;
        var caseSensitive = CaseSensitiveCheck.IsChecked == true;
        var enabled       = EnabledCheck.IsChecked == true;

        if (string.IsNullOrEmpty(pattern)) { StatusText.Text = "Pattern is required."; return; }

        try { _ = new Regex(pattern); }
        catch (RegexParseException ex) { StatusText.Text = $"Invalid regex: {ex.Message}"; return; }

        _engine.RemoveRule(pattern);
        _engine.AddRule(pattern, replacement, caseSensitive, enabled, className);
        Refresh();
        _onChanged?.Invoke();
        StatusText.Text = "Saved.";
    }

    private void OnDelete(object? sender, RoutedEventArgs e)
    {
        if (_engine is null) return;
        if (ItemsList.SelectedItem is not SubstituteRow row) { StatusText.Text = "Select a substitute to delete."; return; }
        _engine.RemoveRule(row.Pattern);
        ClearForm();
        Refresh();
        _onChanged?.Invoke();
        StatusText.Text = "Deleted.";
    }

    private void OnToggle(object? sender, RoutedEventArgs e)
    {
        if (_engine is null) return;
        if (ItemsList.SelectedItem is not SubstituteRow row) { StatusText.Text = "Select a substitute to toggle."; return; }
        var rule = _engine.Rules.FirstOrDefault(r => r.Pattern == row.Pattern);
        if (rule is null) return;
        rule.IsEnabled = !rule.IsEnabled;
        Refresh();
        _onChanged?.Invoke();
        StatusText.Text = $"Substitute {(rule.IsEnabled ? "enabled" : "disabled")}.";
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
            Title         = "Import Substitutes",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Substitute files") { Patterns = ["*.cfg", "*.txt"] }
            }
        });
        if (files is null || files.Count == 0) return;

        var path = files[0].TryGetLocalPath();
        if (string.IsNullOrEmpty(path)) return;

        var result = Genie4Importer.ImportSubstitutes(path, _engine, ImportMode.Merge);
        Refresh();
        _onChanged?.Invoke();
        StatusText.Text = result.Skipped > 0
            ? $"Imported {result.Imported} substitute(s), skipped {result.Skipped}."
            : $"Imported {result.Imported} substitute(s).";
    }

    private void ClearForm()
    {
        ItemsList.SelectedItem       = null;
        PatternBox.Text              = string.Empty;
        ReplacementBox.Text          = string.Empty;
        ClassBox.Text                = string.Empty;
        CaseSensitiveCheck.IsChecked = false;
        EnabledCheck.IsChecked       = true;
        StatusText.Text              = string.Empty;
    }
}
