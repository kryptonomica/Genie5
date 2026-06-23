using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reactive.Linq;
using Genie.Core;
using Genie.Core.Events;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Genie.App.ViewModels;

/// <summary>
/// Backs the dockable Players panel — other players currently in the room, one
/// per line (Genie 3/4 "Players window" parity, issue #86).
///
/// DR sends them as a single <c>room players</c> component string (e.g.
/// "Also here: X, Y and Z."), so we keep the raw line AND a best-effort split
/// into individual entries for the list view.
///
/// Hidden by default; re-open via Window → Players.
/// </summary>
public sealed class PlayersViewModel : ReactiveObject
{
    /// <summary>Individual player entries parsed from the room-players line.</summary>
    public ObservableCollection<string> Players { get; } = new();

    /// <summary>The raw <c>room players</c> text, e.g. "Also here: Naper." Kept
    /// as a subtitle / debugging aid alongside the parsed list.</summary>
    [Reactive] public string RawText { get; private set; } = "";

    /// <summary>Player count.</summary>
    [Reactive] public int    Count   { get; private set; }

    /// <summary>True when no other players are present — drives the empty-state
    /// placeholder.</summary>
    [Reactive] public bool   IsEmpty { get; private set; } = true;

    public void Attach(GenieCore core)
    {
        // Two carriers, one subscription so they stay ordered on the UI thread:
        //   • "room players" → (re)populate the list.
        //   • "room title"   → a NEW room arrived; clear first. DR sends the room
        //     title for every room (always BEFORE the "room players" line in the
        //     room block) but OMITS the "room players" component entirely when no
        //     one else is present — so without this, walking from a populated room
        //     into an empty one would leave the previous room's occupants showing
        //     (issue #86). A populated room re-fills immediately after the clear.
        core.GameEvents
            .OfType<ComponentEvent>()
            .Where(e => string.Equals(e.ComponentId, "room players", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(e.ComponentId, "room title",   StringComparison.OrdinalIgnoreCase))
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(e => Refresh(
                string.Equals(e.ComponentId, "room players", StringComparison.OrdinalIgnoreCase)
                    ? e.Content
                    : ""));   // room title = new room → clear until its players line (if any) arrives
    }

    private void Refresh(string? content)
    {
        RawText = content ?? "";
        Players.Clear();
        foreach (var name in ParseNames(RawText)) Players.Add(name);
        Count   = Players.Count;
        IsEmpty = Players.Count == 0;
    }

    // Heuristic split of DR's natural-language room-players line into entries:
    //   "Also here: X."            → [X]
    //   "Also here: X and Y."      → [X, Y]
    //   "Also here: X, Y and Z."   → [X, Y, Z]
    // Good enough for a list display. A later pass could read the <a> player
    // links the parser already sees for exactness (and to enable click-to-target).
    private static IEnumerable<string> ParseNames(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) yield break;

        var s = raw.Trim();

        // Strip DR's standard "Also here:" lead-in (only an early-string colon —
        // never split on a colon that appears inside a name/title).
        var colon = s.IndexOf(':');
        if (colon is >= 0 and < 12) s = s[(colon + 1)..];

        s = s.Trim().TrimEnd('.').Trim();
        if (s.Length == 0) yield break;

        // Fold the "… and Z" tail into a comma, then split on commas.
        s = s.Replace(" and ", ", ");
        foreach (var part in s.Split(','))
        {
            var name = part.Trim();
            // Strip DR's per-player postural/descriptive clause: "Foo who is
            // sitting", "Bar who is lying down", "Baz who has a stony visage",
            // "Qux who is shrouded in ghostly flames". Real names never contain
            // the literal token " who ", so the first occurrence is a safe cut
            // and keeps the title-prefixed name (e.g. "Empath Alafret").
            var who = name.IndexOf(" who ", StringComparison.Ordinal);
            if (who >= 0) name = name[..who].Trim();
            if (name.Length > 0) yield return name;
        }
    }
}
