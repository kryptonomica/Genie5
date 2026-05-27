using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using Genie.Core.Mapper;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Genie.App.ViewModels;

/// <summary>
/// View-model for the Cross-Zone Connections editor dialog. Shows the
/// full list of connections from <c>ZoneConnections.xml</c>; the user
/// can add / edit / delete entries. Save persists back to disk.
///
/// <para>
/// Connections drive the multi-zone pathfinder
/// (<see cref="MultiZonePathfinder"/>). Each row is one transit link
/// between two zones: "board boat at Crossing docks → arrive Throne
/// City pier, 5-10 min wait, no requirements."
/// </para>
/// </summary>
public sealed class ZoneConnectionsViewModel : ReactiveObject
{
    private readonly ZoneConnectionsRepository _repo;

    public ObservableCollection<ZoneConnection> Connections { get; } = new();

    [Reactive] public ZoneConnection? Selected { get; set; }

    [Reactive] public string Status { get; private set; } = "";

    public ReactiveCommand<Unit, Unit>  AddCommand    { get; }
    public ReactiveCommand<Unit, Unit>  RemoveCommand { get; }
    public ReactiveCommand<Unit, bool>  SaveCommand   { get; }
    public ReactiveCommand<Unit, bool>  CloseCommand  { get; }

    public ZoneConnectionsViewModel(ZoneConnectionsRepository repo)
    {
        _repo = repo;
        foreach (var c in _repo.Load()) Connections.Add(c);

        AddCommand = ReactiveCommand.Create(() =>
        {
            // Insert a stub connection that the user fills in. Default
            // verb suggests boats — the most common use case — but the
            // user can change any field.
            var fresh = new ZoneConnection
            {
                Verb        = "board boat",
                TransitType = "boat",
            };
            Connections.Add(fresh);
            Selected = fresh;
        });

        var canRemove = this.WhenAnyValue(x => x.Selected).Select(s => s is not null);
        RemoveCommand = ReactiveCommand.Create(() =>
        {
            if (Selected is not null) Connections.Remove(Selected);
        }, canRemove);

        SaveCommand = ReactiveCommand.Create(() =>
        {
            try
            {
                _repo.Save(Connections);
                Status = $"Saved {Connections.Count} connection{(Connections.Count == 1 ? "" : "s")}.";
                return true;
            }
            catch (Exception ex)
            {
                Status = $"Save failed: {ex.Message}";
                return false;
            }
        });

        CloseCommand = ReactiveCommand.Create(() => false);
    }
}
