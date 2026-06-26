using Dock.Model.Mvvm.Controls;
using Genie.App.ViewModels;
using Genie.Core.Layout;

namespace Genie.App.Docking;

/// <summary>
/// Scene tool — shows DR room/scene artwork (the <c>&lt;resource picture&gt;</c>
/// image). Hidden by default; re-open via Window → Scene. Only the title syncs
/// from <see cref="WindowSettings"/>; the image fills the panel.
/// </summary>
public class SceneTool : Tool, IWindowMenuHost
{
    public SceneViewModel ViewModel { get; }

    /// <summary>Right-click window menu (Close), built by <see cref="GenieDockFactory"/>.</summary>
    public WindowMenuModel? WindowMenu { get; set; }

    public SceneTool(SceneViewModel vm, WindowSettings? settings = null)
    {
        ViewModel = vm;
        Id        = "scene";
        Title     = "Scene";

        if (settings is not null)
        {
            ApplyTitle(settings);
            settings.Changed += () => ApplyTitle(settings);
        }
    }

    private void ApplyTitle(WindowSettings s) =>
        Title = string.IsNullOrEmpty(s.DisplayTitle) ? s.DefaultTitle : s.DisplayTitle;
}
