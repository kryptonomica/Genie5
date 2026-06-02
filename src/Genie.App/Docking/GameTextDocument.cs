using Avalonia;
using Avalonia.Media;
using Dock.Model.ReactiveUI.Controls;
using Genie.App.Controls;
using Genie.App.ViewModels;
using Genie.Core.Layout;
using ReactiveUI.Fody.Helpers;

namespace Genie.App.Docking;

public class GameTextDocument : Document
{
    public GameTextViewModel ViewModel { get; }

    /// <summary>Per-window foreground brush. Null falls through to the global GameBrush.</summary>
    [Reactive] public IBrush?    ToolForeground { get; private set; }

    /// <summary>Per-window background brush. Null = transparent (default).</summary>
    [Reactive] public IBrush?    ToolBackground { get; private set; }

    /// <summary>Per-window font family override.</summary>
    [Reactive] public FontFamily ToolFontFamily { get; private set; } = new("Cascadia Mono,Consolas,Courier New,monospace");

    /// <summary>Per-window font size override.</summary>
    [Reactive] public double     ToolFontSize   { get; private set; } = 13;

    public GameTextDocument(GameTextViewModel vm, WindowSettings? settings = null)
    {
        ViewModel = vm;
        Id        = "game-text";
        Title     = "Game";

        if (settings is not null)
        {
            ApplySettings(settings);
            settings.Changed += () => ApplySettings(settings);
        }
    }

    /// <summary>
    /// Copy values from <see cref="WindowSettings"/> into the reactive UI
    /// properties. Called once at construction and re-called on every
    /// <c>Changed</c> notification so Layout-tab edits repaint live.
    /// </summary>
    private void ApplySettings(WindowSettings s)
    {
        Title          = string.IsNullOrEmpty(s.DisplayTitle) ? s.DefaultTitle : s.DisplayTitle;
        // Resolve per-window sentinels (empty FontFamily / non-positive
        // FontSize / "Default" Foreground) against the global DisplaySettings
        // values pushed to Application.Resources. Option A: per-window
        // overrides global only when explicitly set. See
        // WindowSettingsResolver for the full sentinel table.
        ToolFontFamily = WindowSettingsResolver.ResolveFontFamily(s.FontFamily);
        ToolFontSize   = WindowSettingsResolver.ResolveFontSize(s.FontSize);
        ToolForeground = WindowSettingsResolver.ResolveForeground(s.Foreground);
        ToolBackground = WindowSettingsResolver.ResolveBackground(s.Background); // null = transparent
    }
}
