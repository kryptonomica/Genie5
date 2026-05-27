using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Genie.App.Controls;
using Genie.App.Highlighting;
using Genie.Core.Highlights;
using Genie.Core.Import;

namespace Genie.App.Views;

/// <summary>
/// Highlights editor panel — code-behind + named controls, no MVVM.
/// Ported from dylb0t/Genie5's <c>HighlightStringsPanel</c>. The DataGrid uses
/// runtime bindings against an anonymous row record; the editor reads / writes
/// directly to the engine through named controls. This pattern sidesteps every
/// Avalonia binding pitfall we hit with the previous MVVM-based editor.
/// </summary>
public partial class HighlightStringsPanel : UserControl
{
    public sealed record HighlightRow(
        string EnabledGlyph, string MatchType, string ForegroundColor, string BackgroundColor,
        string Pattern, string ClassName)
    {
        /// <summary>Brush parsed from <see cref="ForegroundColor"/> so the
        /// Pattern cell can render in the actual highlight colour.</summary>
        public IBrush PatternForeground => ColorPickerHelpers.ParseBrush(ForegroundColor) ?? Brushes.LightGray;

        /// <summary>Brush parsed from <see cref="BackgroundColor"/> so the
        /// Pattern cell can render against the actual highlight background.</summary>
        public IBrush PatternBackground => ColorPickerHelpers.ParseBrush(BackgroundColor) ?? Brushes.Transparent;
    }

    private static readonly string[] MatchTypes =
        ["String", "Line", "BeginsWith", "Regex"];

    private HighlightEngine? _engine;
    private Action?          _onRulesChanged;

    public HighlightStringsPanel()
    {
        InitializeComponent();
        MatchTypeBox.ItemsSource   = MatchTypes;
        MatchTypeBox.SelectedIndex = 0;
        FgColorPicker.Color        = Colors.Yellow;
        BgNoneCheck.IsChecked      = true;
    }

    /// <summary>
    /// Wire the panel up to a live engine. <paramref name="onRulesChanged"/> is
    /// fired after every mutation (Save / Delete / Toggle / Import) so the host
    /// can repaint already-rendered text against the new rule set.
    /// </summary>
    public void Initialize(HighlightEngine engine, Action? onRulesChanged = null)
    {
        _engine         = engine;
        _onRulesChanged = onRulesChanged;
        Refresh();
    }

    private void Refresh()
    {
        if (_engine is null) return;
        var keep = (ItemsList.SelectedItem as HighlightRow)?.Pattern;
        ItemsList.ItemsSource = _engine.Rules
            .Select(r => new HighlightRow(
                r.IsEnabled ? "✓" : "✗",
                r.MatchType.ToString(),
                r.ForegroundColor,
                r.BackgroundColor,
                r.Pattern,
                r.ClassName))
            .ToList();
        if (keep is not null)
            ItemsList.SelectedItem = ((IEnumerable<HighlightRow>)ItemsList.ItemsSource)
                .FirstOrDefault(r => r.Pattern == keep);
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_engine is null || ItemsList.SelectedItem is not HighlightRow row) return;
        var rule = _engine.Rules.FirstOrDefault(r => r.Pattern == row.Pattern);
        if (rule is null) return;
        PatternBox.Text              = rule.Pattern;
        ColorPickerHelpers.LoadColor(FgColorPicker, FgDefaultCheck, rule.ForegroundColor, "Default");
        ColorPickerHelpers.LoadColor(BgColorPicker, BgNoneCheck,    rule.BackgroundColor, "");
        MatchTypeBox.SelectedItem    = rule.MatchType.ToString();
        ClassBox.Text                = rule.ClassName;
        CaseSensitiveCheck.IsChecked = rule.CaseSensitive;
        EnabledCheck.IsChecked       = rule.IsEnabled;
        StatusText.Text              = string.Empty;
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        if (_engine is null) return;
        var pattern       = PatternBox.Text?.Trim() ?? string.Empty;
        var color         = ColorPickerHelpers.ReadColor(FgColorPicker, FgDefaultCheck, "Default");
        var bgColor       = ColorPickerHelpers.ReadColor(BgColorPicker, BgNoneCheck,    "");
        var matchTypeStr  = MatchTypeBox.SelectedItem as string ?? "String";
        var matchType     = Enum.TryParse<HighlightMatchType>(matchTypeStr, out var mt) ? mt : HighlightMatchType.String;
        var className     = ClassBox.Text?.Trim() ?? string.Empty;
        var caseSensitive = CaseSensitiveCheck.IsChecked == true;
        var enabled       = EnabledCheck.IsChecked == true;

        if (string.IsNullOrEmpty(pattern)) { StatusText.Text = "Pattern is required."; return; }

        if (matchType == HighlightMatchType.Regex)
        {
            try { _ = new Regex(pattern); }
            catch (RegexParseException ex) { StatusText.Text = $"Invalid regex: {ex.Message}"; return; }
        }

        _engine.RemoveRule(pattern);
        _engine.AddRule(pattern, color, bgColor, matchType, caseSensitive, enabled, className);
        Refresh();
        _onRulesChanged?.Invoke();
        UserHighlights.NotifyRulesChanged();
        StatusText.Text = $"Saved “{pattern}” → {color}.";
    }

    private void OnDelete(object? sender, RoutedEventArgs e)
    {
        if (_engine is null) return;
        if (ItemsList.SelectedItem is not HighlightRow row) { StatusText.Text = "Select a highlight to delete."; return; }
        _engine.RemoveRule(row.Pattern);
        ClearForm();
        Refresh();
        _onRulesChanged?.Invoke();
        UserHighlights.NotifyRulesChanged();
        StatusText.Text = "Deleted.";
    }

    private void OnToggle(object? sender, RoutedEventArgs e)
    {
        if (_engine is null) return;
        if (ItemsList.SelectedItem is not HighlightRow row) { StatusText.Text = "Select a highlight to toggle."; return; }
        var rule = _engine.Rules.FirstOrDefault(r => r.Pattern == row.Pattern);
        if (rule is null) return;
        rule.IsEnabled = !rule.IsEnabled;
        Refresh();
        _onRulesChanged?.Invoke();
        UserHighlights.NotifyRulesChanged();
        StatusText.Text = $"Highlight {(rule.IsEnabled ? "enabled" : "disabled")}.";
    }

    private void OnAdd(object? sender, RoutedEventArgs e) => ClearForm();

    private void OnClear(object? sender, RoutedEventArgs e) => ClearForm();

    private async void OnImport(object? sender, RoutedEventArgs e)
    {
        if (_engine is null) return;

        var parent = this.GetVisualRoot() as Window;
        if (parent?.StorageProvider is not { } sp) return;

        // Avalonia 11's modern file-picker API. The dylb0t source used the
        // deprecated OpenFileDialog; this is the supported replacement.
        var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title         = "Import Highlights",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Highlight files") { Patterns = ["*.cfg", "*.txt"] }
            }
        });
        if (files is null || files.Count == 0) return;

        var path = files[0].TryGetLocalPath();
        if (string.IsNullOrEmpty(path)) return;

        var result = Genie4Importer.ImportHighlights(path, _engine, ImportMode.Merge);
        Refresh();
        _onRulesChanged?.Invoke();
        UserHighlights.NotifyRulesChanged();
        StatusText.Text = result.Skipped > 0
            ? $"Imported {result.Imported} highlight(s), skipped {result.Skipped}."
            : $"Imported {result.Imported} highlight(s).";
    }

    private void ClearForm()
    {
        ItemsList.SelectedItem       = null;
        PatternBox.Text              = string.Empty;
        FgColorPicker.Color          = Colors.Yellow;
        FgDefaultCheck.IsChecked     = false;
        BgNoneCheck.IsChecked        = true;
        MatchTypeBox.SelectedIndex   = 0;
        ClassBox.Text                = string.Empty;
        CaseSensitiveCheck.IsChecked = false;
        EnabledCheck.IsChecked       = true;
        StatusText.Text              = string.Empty;
    }
}
