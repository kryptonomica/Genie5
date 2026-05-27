using Avalonia;
using Avalonia.Media;
using Dock.Model.ReactiveUI.Controls;
using Genie.App.Controls;
using Genie.App.ViewModels;
using Genie.Core.Layout;
using ReactiveUI.Fody.Helpers;

namespace Genie.App.Docking;

public class BackpackTool : Tool
{
    public InventoryViewModel ViewModel { get; }

    [Reactive] public IBrush?    ToolForeground { get; private set; }
    [Reactive] public IBrush?    ToolBackground { get; private set; }
    [Reactive] public FontFamily ToolFontFamily { get; private set; } = new("Cascadia Mono,Consolas,Courier New,monospace");
    [Reactive] public double     ToolFontSize   { get; private set; } = 11;

    public BackpackTool(InventoryViewModel vm, WindowSettings? settings = null)
    {
        ViewModel = vm;
        Id        = "backpack";
        Title     = "Backpack";

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
                         ?? new SolidColorBrush(Color.FromRgb(0xCC, 0xDD, 0xDD));   // matches old #cdd
        ToolBackground = ColorPickerHelpers.ParseBrush(s.Background);
    }
}
