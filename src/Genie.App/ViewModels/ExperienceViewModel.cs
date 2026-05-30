using Genie.Core;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Genie.App.ViewModels;

/// <summary>
/// Backs the Experience dock panel. Content is pushed by the Experience
/// plugin via the host's <c>SetWindow("Experience", …)</c> — the App doesn't
/// parse exp data itself, it just renders whatever the plugin produces. This is
/// the named-window seam that keeps plugins free of any UI dependency.
/// </summary>
public class ExperienceViewModel : ReactiveObject
{
    [Reactive] public string Content { get; private set; } =
        "(no experience data yet — train a skill, or type 'exp')";

    public void Attach(GenieCore core)
    {
        core.SetPluginWindow += (window, content) =>
        {
            if (!string.Equals(window, "Experience", StringComparison.OrdinalIgnoreCase)) return;
            Avalonia.Threading.Dispatcher.UIThread.Post(() => Content = content);
        };
    }
}
