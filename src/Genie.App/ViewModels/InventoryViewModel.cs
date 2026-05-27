using System.Collections.ObjectModel;
using System.Reactive.Linq;
using Genie.Core;
using Genie.Core.Events;
using ReactiveUI;

namespace Genie.App.ViewModels;

/// <summary>
/// Backs the Backpack tool. Listens to the <c>inv</c> stream — DR emits each
/// carried item as a <see cref="TextEvent"/> on that stream during login and
/// whenever the player runs <c>inventory</c>.
///
/// The server emits a <see cref="ClearStreamEvent"/> for <c>inv</c> before each
/// re-send (login burst, manual <c>inventory</c> refresh), so the list rebuilds
/// itself without duplicates as long as we honour that signal.
/// </summary>
public class InventoryViewModel : ReactiveObject
{
    /// <summary>
    /// Inventory items wrapped as <see cref="TextLine"/> so the template
    /// can run them through the highlight pipeline (player names, currencies,
    /// any user-defined rules) just like the main game window.
    /// </summary>
    public ObservableCollection<TextLine> Items { get; } = [];

    public void Attach(GenieCore core)
    {
        core.GameEvents
            .OfType<TextEvent>()
            .Where(e => string.Equals(e.Stream, "inv", StringComparison.OrdinalIgnoreCase))
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(e =>
            {
                var line = e.Text?.TrimEnd();
                if (!string.IsNullOrWhiteSpace(line))
                    Items.Add(new TextLine(line, StreamColor.Main));
            });

        core.GameEvents
            .OfType<ClearStreamEvent>()
            .Where(e => string.Equals(e.StreamId, "inv", StringComparison.OrdinalIgnoreCase))
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(_ => Items.Clear());
    }
}
