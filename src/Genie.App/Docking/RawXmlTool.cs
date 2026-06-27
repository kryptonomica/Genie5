using Dock.Model.Mvvm.Controls;
using Genie.App.ViewModels;
using Genie.Core.Layout;

namespace Genie.App.Docking;

/// <summary>
/// Raw XML dock tool (issue #14). A read-only live view of the raw server XML
/// stream — capped rolling buffer, auto-scroll, default hidden. Re-opens via
/// Window → Raw XML. A dev/debug panel, grouped beside the other utility tabs
/// (Scripts / Scene) in the default layout.
/// </summary>
public class RawXmlTool : Tool, IWindowMenuHost
{
    public RawXmlViewModel ViewModel { get; }

    /// <summary>Right-click window menu (Clear / Close), built by
    /// <see cref="GenieDockFactory"/>.</summary>
    public WindowMenuModel? WindowMenu { get; set; }

    public RawXmlTool(RawXmlViewModel vm, WindowSettings? settings = null)
    {
        ViewModel = vm;
        Id        = "raw-xml";
        Title     = "Raw XML";

        if (settings is not null)
        {
            ApplyTitle(settings);
            settings.Changed += () => ApplyTitle(settings);
        }
    }

    private void ApplyTitle(WindowSettings s) =>
        Title = string.IsNullOrEmpty(s.DisplayTitle) ? s.DefaultTitle : s.DisplayTitle;
}
