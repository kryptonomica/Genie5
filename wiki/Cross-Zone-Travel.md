# Cross-Zone Travel

Single-zone walking is handled by the [Mapper](Mapper) — click a room and Genie walks you there. Travelling **across** zone boundaries needs more: a graph that spans multiple zone files plus the transit links (boats, ferries, climb-walls, portals) that connect them. Genie 5 has the pathfinder, the data format, and an editor for that graph.

> **Status:** the transit graph, the multi-zone pathfinder, and the editor are in place. Feeding a full cross-zone route to the walker is the remaining piece (see [What's left](#whats-left)).

## The transit graph

Cross-zone links live in a single **`ZoneConnections.xml`** at the root of your Maps folder, next to the `Map##_*.xml` zone files. Keeping them in one file lets the community Maps repo curate transit links without touching individual zone files.

Each connection is one directed link:

| Field | Meaning |
| --- | --- |
| `from-zone` / `to-zone` | Zone-file basenames without `.xml` (e.g. `Map01_Crossing`). |
| `from-room` / `to-room` | A node id, or `#serverRoomId`. |
| `verb` | What the walker sends (`board boat`, `climb wall`). |
| `transit-type` | A free-form tag (`boat`, `climb`, `ride`, `portal`). |
| `requires` | A skill / class / level gate. |
| `rt` | Roundtime seconds. |
| `wait-min` / `wait-max` | Scheduled-departure wait window (for boats/ferries). |
| `notes` | Community notes. |

```xml
<connections>
  <connection id="boat-cross-throne"
              from-zone="Map01_Crossing"  from-room="#37666999"
              to-zone="Map35_Throne_City" to-room="#37666500"
              verb="board boat" transit-type="boat"
              wait-min="300" wait-max="600"
              requires="" rt="0" notes="" />
</connections>
```

On first launch Genie **seeds** a documented starter template (example routes with placeholder room ids) so you have something to edit, and writes a marker so it never re-seeds — if you delete the file deliberately, Genie respects that. Connections that can't be resolved (stale zone/room refs) are simply skipped, so a half-filled file degrades gracefully to single-zone routing rather than breaking.

## The pathfinder

The multi-zone pathfinder runs **Dijkstra over a meta-graph of (zone, room) pairs**, loading each zone lazily (read at most once per search). It draws edges from two sources:

- **Intra-zone** — each loaded zone's own room exits.
- **Cross-zone** — the `ZoneConnections.xml` links.

Both kinds are gated against your character's live skills, class, and level — an edge you can't take is excluded from the search entirely. Edge weights:

- intra-zone: `1 + RtCost/4`
- cross-zone: `1 + RtCost/4 + averageWait/4`

Wait time dominates, so a boat with a long schedule is only chosen when there's no overland route. The result is an ordered list of steps, each carrying its verb and — for cross-zone hops — the expected wait window and target zone.

Rooms can be referenced by integer node id or by DragonRealms server-room id (`#NNNN`); the pathfinder resolves both, preferring the server-room id since it survives map regeneration.

## The editor

**File → Cross-Zone Connections…** opens a grid editor: add, remove, edit, and save connections. This is the curation surface for the transit graph the pathfinder consults. You can also let the community Maps repo ship richer versions over time — see [Updating Maps & Scripts](Updating-Maps-and-Scripts).

## The walker's wait UI

The walker already understands cross-zone steps: when one carries a wait window, it shows a countdown ("~4:23 left") in the Mapper indicator strip while it waits for the destination zone's room to fingerprint in. So the *presentation* of a cross-zone hop is built.

## What's left

1. **Feed multi-zone plans to the walker** — route through the multi-zone pathfinder when the destination is in a different zone, and translate its step list into the walk loop.
2. **Cross-zone arrival detection** — advance the walk when the destination zone's room becomes current (using the same fingerprinting + auto-load-zone the mapper already does).
3. **A travel entry point** — today a walk is started by clicking a room in the active zone; cross-zone travel needs a destination picker (or named-destination registry) that spans zones.

## A note on scope

Genie 5 deliberately does **not** aim to absorb the entire community `travel.cmd` into the engine. Escape recipes for un-mappable starting rooms, premium-account shortcuts, and ferry-state recovery are well-suited to scripts and stay there. The engine targets the common land + scheduled-transit routes, with scripts remaining the fallback for the long tail.

## Related

- [The Mapper](Mapper) — single-zone tracking and walking.
- [Updating Maps & Scripts](Updating-Maps-and-Scripts) — where `ZoneConnections.xml` and zone files come from.
- The developer design notes: [multi-zone-travel.md](https://github.com/GenieClient/Genie5/blob/main/docs/multi-zone-travel.md), [AUTOMAPPER_DESIGN.md](https://github.com/GenieClient/Genie5/blob/main/docs/AUTOMAPPER_DESIGN.md).
