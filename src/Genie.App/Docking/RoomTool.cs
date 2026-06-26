using Dock.Model.Mvvm.Controls;
using Genie.App.ViewModels;
using Genie.Core.Layout;

namespace Genie.App.Docking;

/// <summary>
/// Room tool — keeps its per-field colours (exits cyan, players green, etc.).
/// Only the title syncs from <see cref="WindowSettings"/>; full background /
/// foreground override would require restructuring the room view to lose the
/// per-field colour coding.
/// </summary>
public class RoomTool : Tool, IWindowMenuHost
{
    public RoomViewModel ViewModel { get; }

    /// <summary>Right-click window menu (Close), built by <see cref="GenieDockFactory"/>.</summary>
    public WindowMenuModel? WindowMenu { get; set; }

    public RoomTool(RoomViewModel vm, WindowSettings? settings = null)
    {
        ViewModel = vm;
        Id        = "room";
        Title     = "Room";

        if (settings is not null)
        {
            ApplyTitle(settings);
            settings.Changed += () => ApplyTitle(settings);
        }
    }

    private void ApplyTitle(WindowSettings s) =>
        Title = string.IsNullOrEmpty(s.DisplayTitle) ? s.DefaultTitle : s.DisplayTitle;
}
