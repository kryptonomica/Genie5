using System.Reactive.Linq;
using Genie.Core;
using Genie.Core.Events;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Genie.App.ViewModels;

public class RoomViewModel : ReactiveObject
{
    [Reactive] public string Title       { get; private set; } = "";
    [Reactive] public string Description { get; private set; } = "";
    [Reactive] public string Exits       { get; private set; } = "";
    [Reactive] public string Players     { get; private set; } = "";
    [Reactive] public string Objects     { get; private set; } = "";

    public void Attach(GenieCore core)
    {
        core.GameEvents.OfType<ComponentEvent>()
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(e =>
            {
                switch (e.ComponentId)
                {
                    case "room title":   Title       = e.Content; break;
                    case "room desc":    Description = e.Content; break;
                    case "room exits":   Exits       = e.Content; break;
                    case "room players": Players     = e.Content; break;
                    case "room objs":    Objects     = e.Content; break;
                }
            });
    }
}
