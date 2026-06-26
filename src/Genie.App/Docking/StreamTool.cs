using Avalonia;
using Avalonia.Media;
using Dock.Model.Mvvm.Controls;
using Genie.App.Controls;
using Genie.App.ViewModels;
using Genie.Core.Layout;

namespace Genie.App.Docking;

public class StreamTool : Tool, IWindowMenuHost
{
    public StreamBuffer Buffer { get; }

    /// <summary>Right-click window menu (Clear / Time Stamp / Name List Only /
    /// Close), built by <see cref="GenieDockFactory"/>.</summary>
    public WindowMenuModel? WindowMenu { get; set; }

    // Per-window appearance overrides. SetProperty-backed (Dock.Model.Mvvm base
    // is a CommunityToolkit ObservableObject) instead of Fody [Reactive].
    private IBrush?    _toolForeground;
    public  IBrush?    ToolForeground { get => _toolForeground; private set => SetProperty(ref _toolForeground, value); }

    private IBrush?    _toolBackground;
    public  IBrush?    ToolBackground { get => _toolBackground; private set => SetProperty(ref _toolBackground, value); }

    private FontFamily _toolFontFamily = new("Cascadia Mono,Consolas,Courier New,monospace");
    public  FontFamily ToolFontFamily { get => _toolFontFamily; private set => SetProperty(ref _toolFontFamily, value); }

    private double     _toolFontSize = 11;
    public  double     ToolFontSize { get => _toolFontSize; private set => SetProperty(ref _toolFontSize, value); }

    public StreamTool(StreamBuffer buffer, WindowSettings? settings = null)
    {
        Buffer = buffer;
        // Plain lowercased buffer name (e.g. "logons", "talk") matches the
        // Window-menu toggle command ids and the WindowSettingsStore keys.
        Id     = buffer.Name.ToLowerInvariant();
        Title  = buffer.Name;

        if (settings is not null)
        {
            // #90: hand the buffer its live settings so StreamBuffer.Add can
            // prepend a timestamp when the Layout-tab toggle is on.
            Buffer.Settings = settings;
            ApplySettings(settings);
            settings.Changed += () => ApplySettings(settings);
        }
    }

    private void ApplySettings(WindowSettings s)
    {
        Title          = string.IsNullOrEmpty(s.DisplayTitle) ? s.DefaultTitle : s.DisplayTitle;
        // Resolve per-window sentinels (empty FontFamily / non-positive
        // FontSize / "Default" Foreground) against the global DisplaySettings
        // values pushed to Application.Resources by DisplaySettings.Apply().
        // Option A: per-window overrides global only when explicitly set.
        ToolFontFamily = WindowSettingsResolver.ResolveFontFamily(s.FontFamily);
        ToolFontSize   = WindowSettingsResolver.ResolveFontSize(s.FontSize);
        ToolForeground = WindowSettingsResolver.ResolveForeground(s.Foreground);
        ToolBackground = WindowSettingsResolver.ResolveBackground(s.Background);
    }
}
