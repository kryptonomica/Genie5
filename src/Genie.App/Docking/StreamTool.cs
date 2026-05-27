using Avalonia;
using Avalonia.Media;
using Dock.Model.ReactiveUI.Controls;
using Genie.App.Controls;
using Genie.App.ViewModels;
using Genie.Core.Layout;
using ReactiveUI.Fody.Helpers;

namespace Genie.App.Docking;

public class StreamTool : Tool
{
    public StreamBuffer Buffer { get; }

    [Reactive] public IBrush?    ToolForeground { get; private set; }
    [Reactive] public IBrush?    ToolBackground { get; private set; }
    [Reactive] public FontFamily ToolFontFamily { get; private set; } = new("Cascadia Mono,Consolas,Courier New,monospace");
    [Reactive] public double     ToolFontSize   { get; private set; } = 11;

    public StreamTool(StreamBuffer buffer, WindowSettings? settings = null)
    {
        Buffer = buffer;
        // Plain lowercased buffer name (e.g. "logons", "talk") matches the
        // Window-menu toggle command ids and the WindowSettingsStore keys.
        Id     = buffer.Name.ToLowerInvariant();
        Title  = buffer.Name;

        if (settings is not null)
        {
            ApplySettings(settings);
            settings.Changed += () => ApplySettings(settings);
        }
    }

    private void ApplySettings(WindowSettings s)
    {
        Title          = string.IsNullOrEmpty(s.DisplayTitle) ? s.DefaultTitle : s.DisplayTitle;
        ToolFontFamily = new FontFamily(s.FontFamily);
        ToolFontSize   = s.FontSize;
        ToolForeground = ColorPickerHelpers.ParseBrush(s.Foreground)
                         ?? Application.Current?.Resources["GameBrush"] as IBrush
                         ?? Brushes.LightGray;
        ToolBackground = ColorPickerHelpers.ParseBrush(s.Background);
    }
}
