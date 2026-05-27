using System.Reactive;
using Avalonia.Media;
using Genie.App.Settings;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Genie.App.ViewModels;

/// <summary>
/// View-model for the Display Settings dialog. Edits a local copy of the
/// live <see cref="DisplaySettings"/>; <see cref="OkCommand"/> commits the
/// changes (which the owner persists to disk).
/// </summary>
public class DisplaySettingsViewModel : ReactiveObject
{
    private readonly DisplaySettings _live;

    /// <summary>
    /// All font families installed on the host OS, plus the currently-selected
    /// font even if it isn't installed (so the binding shows it as selected).
    /// </summary>
    public IReadOnlyList<string> SystemFonts { get; }

    [Reactive] public Color  GameColor  { get; set; }
    [Reactive] public Color  EchoColor  { get; set; }
    [Reactive] public bool   EchoItalic { get; set; }
    [Reactive] public string FontFamily { get; set; }
    [Reactive] public double FontSize   { get; set; }

    /// <summary>
    /// Path to the user's external editor for "Edit Script" (the pencil
    /// icon on the Script Bar, plus <c>#edit foo</c>). Empty means "use
    /// OS default `.cmd` file handler" — typically Notepad on Windows,
    /// TextEdit on macOS, the desktop default on Linux.
    /// </summary>
    [Reactive] public string EditorPath { get; set; } = "";

    public ReactiveCommand<Unit, bool> OkCommand     { get; }
    public ReactiveCommand<Unit, bool> CancelCommand { get; }
    public ReactiveCommand<Unit, Unit> ResetCommand  { get; }

    public DisplaySettingsViewModel() : this(new DisplaySettings()) { }

    public DisplaySettingsViewModel(DisplaySettings live)
    {
        _live = live;

        // The stored value may be a comma-separated fallback chain ("Consolas,
        // Courier New,monospace"). Picking from a dropdown only sets one name,
        // so take the head of the chain as the current selection.
        var currentFont = live.FontFamily.Split(',')[0].Trim();

        SystemFonts = LoadSystemFonts(currentFont);

        GameColor  = TryParseColor(live.GameColorHex, Avalonia.Media.Color.FromRgb(0xCC, 0xCC, 0xCC));
        EchoColor  = TryParseColor(live.EchoColorHex, Avalonia.Media.Color.FromRgb(0x88, 0xBB, 0xCC));
        EchoItalic = live.EchoItalic;
        FontFamily = currentFont;
        FontSize   = live.FontSize;
        EditorPath = live.EditorPath;

        OkCommand     = ReactiveCommand.Create(Commit);
        CancelCommand = ReactiveCommand.Create(() => false);
        ResetCommand  = ReactiveCommand.Create(ResetToDefaults);
    }

    private bool Commit()
    {
        _live.GameColorHex = "#" + GameColor.ToString().Substring(3); // drop alpha → #RRGGBB
        _live.EchoColorHex = "#" + EchoColor.ToString().Substring(3);
        _live.EchoItalic   = EchoItalic;
        _live.FontFamily   = FontFamily;
        _live.FontSize     = FontSize;
        _live.EditorPath   = EditorPath ?? "";
        return true;
    }

    private void ResetToDefaults()
    {
        var d = new DisplaySettings();
        GameColor  = TryParseColor(d.GameColorHex, Colors.LightGray);
        EchoColor  = TryParseColor(d.EchoColorHex, Avalonia.Media.Color.FromRgb(0x88, 0xBB, 0xCC));
        EchoItalic = d.EchoItalic;
        FontFamily = d.FontFamily.Split(',')[0].Trim();
        FontSize   = d.FontSize;
        EditorPath = d.EditorPath;
    }

    // ── System font enumeration (cross-platform via FontManager) ──────────────

    private static IReadOnlyList<string> LoadSystemFonts(string includeIfMissing)
    {
        // FontManager.Current.SystemFonts uses the platform's font enumerator:
        //   Windows → DirectWrite, macOS → CoreText, Linux → FontConfig.
        // No OS-specific code needed here.
        var fonts = FontManager.Current.SystemFonts
            .Select(f => f.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!string.IsNullOrWhiteSpace(includeIfMissing) &&
            !fonts.Contains(includeIfMissing, StringComparer.OrdinalIgnoreCase))
        {
            fonts.Add(includeIfMissing);
        }

        fonts.Sort(StringComparer.OrdinalIgnoreCase);
        return fonts;
    }

    private static Color TryParseColor(string hex, Color fallback)
        => Color.TryParse(hex, out var c) ? c : fallback;
}
