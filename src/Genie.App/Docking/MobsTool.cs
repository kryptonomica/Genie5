using Dock.Model.Mvvm.Controls;
using Genie.App.ViewModels;
using Genie.Core.Layout;

namespace Genie.App.Docking;

/// <summary>
/// Mobs tool — lists the creatures in the room (issue #86). Hidden by default;
/// re-open via Window → Mobs. Only the title syncs from
/// <see cref="WindowSettings"/>; the list keeps its own colour coding.
/// </summary>
public class MobsTool : Tool
{
    public MobsViewModel ViewModel { get; }

    public MobsTool(MobsViewModel vm, WindowSettings? settings = null)
    {
        ViewModel = vm;
        Id        = "mobs";
        Title     = "Mobs";

        if (settings is not null)
        {
            ApplyTitle(settings);
            settings.Changed += () => ApplyTitle(settings);
        }
    }

    private void ApplyTitle(WindowSettings s) =>
        Title = string.IsNullOrEmpty(s.DisplayTitle) ? s.DefaultTitle : s.DisplayTitle;
}
