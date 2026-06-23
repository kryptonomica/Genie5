using System;
using System.Collections.ObjectModel;
using System.Reactive.Linq;
using Genie.Core;
using Genie.Core.Events;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Genie.App.ViewModels;

/// <summary>
/// Backs the dockable Mobs panel — the creatures currently in the room, one per
/// line (Genie 3/4 "Mobs window" parity, issue #86).
///
/// Sourced from the engine's already-filtered <c>Room.Creatures</c> list (the
/// bold creature phrases from <c>room objs</c> minus the ignore list — the same
/// data behind <c>$monsterlist</c>/<c>$monstercount</c>), NOT the raw
/// room-objects text, which also contains non-creature ground items.
///
/// Hidden by default; re-open via Window → Mobs.
/// </summary>
public sealed class MobsViewModel : ReactiveObject
{
    private GenieCore? _core;

    /// <summary>Creature phrases in the room, e.g. "a brown lynx that is
    /// sleeping". Rebuilt on every <c>room objs</c> update.</summary>
    public ObservableCollection<string> Mobs { get; } = new();

    /// <summary>Creature count — mirrors <c>$monstercount</c>. Drives the panel
    /// header.</summary>
    [Reactive] public int  Count   { get; private set; }

    /// <summary>True when no creatures are present — drives the empty-state
    /// placeholder.</summary>
    [Reactive] public bool IsEmpty { get; private set; } = true;

    public void Attach(GenieCore core)
    {
        _core = core;

        // "room objs" is the carrier for creatures. GameStateEngine processes
        // the same event first (it subscribes in GenieCore's ctor, before the
        // App attaches) and replaces Room.Creatures, so by the time this
        // UI-thread handler runs the filtered list is ready to read — the same
        // ordering guarantee the $monsterlist script sync relies on.
        core.GameEvents
            .OfType<ComponentEvent>()
            .Where(e => string.Equals(e.ComponentId, "room objs", StringComparison.OrdinalIgnoreCase))
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(_ => Refresh());
    }

    private void Refresh()
    {
        var creatures = _core?.State.Room.Creatures ?? Array.Empty<string>();
        Mobs.Clear();
        foreach (var c in creatures) Mobs.Add(c);
        Count   = Mobs.Count;
        IsEmpty = Mobs.Count == 0;
    }
}
