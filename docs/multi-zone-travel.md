# Multi-Zone Travel

**Status:** infrastructure implemented; walker integration in progress.

Unlike the single-zone walk (which [AutoMapperEngine.FindPath](../src/Genie.Core/Mapper/AutoMapperEngine.cs#L547) and [AutoWalkService](../src/Genie.App/Services/AutoWalkService.cs) already drive end-to-end — see [mapper.md](mapper.md)), travelling across zone boundaries needs a graph that spans multiple zone files plus the transit links (boats, ferries, climb-walls, portals) that connect them. Genie 5 has the pathfinder, the data model, the on-disk format, and a UI editor for that graph. What's not yet wired is the **walker consuming a multi-zone plan** — `AutoWalkService.Start` currently calls the single-zone `FindPath`. This page documents what exists and what remains.

## The pieces that exist

### MultiZonePathfinder

[MultiZonePathfinder](../src/Genie.Core/Mapper/MultiZonePathfinder.cs) is **Dijkstra over a meta-graph of `(zoneFile, room)` tuples**. It loads zones lazily (each read at most once per search) and draws edges from two sources:

- **Intra-zone** — each loaded zone's `MapNode.Exits`.
- **Cross-zone** — [ZoneConnection](../src/Genie.Core/Mapper/ZoneConnection.cs)s from a [ZoneConnectionsRepository](../src/Genie.Core/Mapper/ZoneConnectionsRepository.cs).

Both edge kinds honour [ExitRequirement](../src/Genie.Core/Mapper/ExitRequirement.cs) against the character's live `SkillStore` / class / level — an edge the character can't take is excluded from the search entirely. Weights:

- intra-zone: `1 + RtCost/4`
- cross-zone: `1 + RtCost/4 + averageWait/4`

Wait time dominates, so a boat with a long schedule is only preferred when there's no overland route. The result is a [MultiZonePath](../src/Genie.Core/Mapper/MultiZonePathfinder.cs#L30): an ordered list of [WalkStep](../src/Genie.Core/Mapper/MultiZonePathfinder.cs#L12)s (each carrying its verb, and for cross-zone hops the expected wait window + target zone), plus a `HasCrossZoneHop` flag.

Rooms are referenced by either integer node id or DR server-room id (`#NNNN`); `TryMatchRoom` resolves both, preferring `ServerRoomId` since it survives map regeneration.

### ZoneConnection data + ZoneConnections.xml

A [ZoneConnection](../src/Genie.Core/Mapper/ZoneConnection.cs) is one directed link:

| Field | Meaning |
| --- | --- |
| `FromZone` / `ToZone` | zone-file basenames **without** `.xml` (`Map01_Crossing`) |
| `FromRoom` / `ToRoom` | node id or `#serverRoomId` |
| `Verb` | what the walker sends (`board boat`, `climb wall`) |
| `TransitType` | free-form tag (`boat`, `climb`, `ride`, `portal`) |
| `Requires` | skill/class/level gate, parsed by `ExitRequirement` |
| `RtCost` | roundtime seconds |
| `WaitMin` / `WaitMax` | scheduled-departure wait window |
| `Notes` | community notes |

These live in a single **`ZoneConnections.xml`** at the root of the Maps directory (next to the `Map##_*.xml` zone files), so the community Maps repo can curate transit links without editing individual zone files. The schema:

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

[ZoneConnectionsRepository](../src/Genie.Core/Mapper/ZoneConnectionsRepository.cs) reads and writes this file. On first launch it **seeds** an embedded baseline ([ZoneConnections.baseline.xml](../src/Genie.Core/Mapper/Resources/ZoneConnections.baseline.xml)) — a documented set of example routes with TODO room ids — so users have a starting template. A `.genie5-zone-connections-seeded` marker ensures it never re-seeds: if you delete the file after seeing it, the app respects that. Unresolvable connections (stale zone/room refs) are silently skipped by the pathfinder, so a half-filled file degrades gracefully to single-zone routing rather than breaking.

### The editor UI

**File → Cross-Zone Connections…** opens the [ZoneConnectionsViewModel](../src/Genie.App/ViewModels/ZoneConnectionsViewModel.cs) grid — add / remove / edit / save connections, round-tripping through the repository. This is the curation surface for the meta-graph the pathfinder consults.

### Walker wait UI (ready)

[AutoWalkService](../src/Genie.App/Services/AutoWalkService.cs) already understands cross-zone `WalkStep`s: `DispatchNextStep` surfaces the expected wait window and runs a countdown ("~4:23 left") in the Mapper indicator strip while waiting for the destination zone to fingerprint in. So the walker's *presentation* of a cross-zone hop is built — it just isn't being fed multi-zone plans yet.

## What remains

1. **Feed multi-zone plans to the walker.** `AutoWalkService.Start` (or a new entry point) needs to call `MultiZonePathfinder.FindPath` when the destination is in a different zone, and translate the resulting `WalkStep` list into the walk loop (it currently takes a `List<string>` of moves from the single-zone `FindPath`).
2. **Cross-zone arrival detection.** On a cross-zone step, the walk advances when the destination zone's room becomes current (the same fingerprint resolution `OnRoomChanged` already does, plus the `RoomNotFoundInZone` → auto-load-zone path in the mapper).
3. **A travel entry point.** Today the only walk trigger is clicking a room in the active zone. Cross-zone travel needs either a destination picker that spans zones or a named-destination registry.

## Design alignment

The broader plan — skill-weighted paths, the user-editable connection database, transit modelling, and the phased rollout — lives in [AUTOMAPPER_DESIGN.md](AUTOMAPPER_DESIGN.md). This page tracks the multi-zone slice of that work specifically.

Note that Genie 5 deliberately does **not** aim to port the whole of the community `travel.cmd` into the engine. Escape recipes for un-mappable starting rooms, premium-account shortcuts, and ferry-state recovery are well-suited to scripts and stay there; the engine version targets the common land + scheduled-transit routes, with the script remaining the fallback for the long tail.

## Code references

- **[MultiZonePathfinder.cs](../src/Genie.Core/Mapper/MultiZonePathfinder.cs)** — lazy-loading Dijkstra, `WalkStep` / `MultiZonePath`.
- **[ZoneConnection.cs](../src/Genie.Core/Mapper/ZoneConnection.cs)** — the cross-zone edge model.
- **[ZoneConnectionsRepository.cs](../src/Genie.Core/Mapper/ZoneConnectionsRepository.cs)** — `ZoneConnections.xml` I/O + first-launch seeding.
- **[ZoneConnections.baseline.xml](../src/Genie.Core/Mapper/Resources/ZoneConnections.baseline.xml)** — the embedded starter template.
- **[ZoneConnectionsViewModel.cs](../src/Genie.App/ViewModels/ZoneConnectionsViewModel.cs)** — the editor.
- **[AutoWalkService.cs](../src/Genie.App/Services/AutoWalkService.cs)** — the walker (with cross-zone wait UI ready).
