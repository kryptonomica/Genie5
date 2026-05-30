# Genie 5 Roadmap

This is the public roadmap — what's shipped in the current alpha, what's in
flight, what's planned, and what's deliberately deferred. The goal is to
keep contributors and alpha testers oriented on where the project is going
and where help is most welcome.

Items marked **🚧 In flight** have someone actively working on them. Items
marked **🗓 Planned** are scoped and ready for someone to pick up. Items
marked **💭 Considering** are open design questions where we'd welcome an
issue with ideas before code lands.

If you want to work on a 🗓 Planned item, open an issue first so we don't
end up with two parallel PRs.

## Alpha 1 — shipped

These work today, in the v5.0.0-alpha.1 build.

- **Connection layer** — SGE direct auth, Lich 5 proxy, dev-replay from
  recorded XML sessions
- **StormFront XML parser** with 22+ typed event records; handles all the
  edge cases documented in [SGE_PROTOCOL.md](SGE_PROTOCOL.md)
- **GameState engine** — live snapshot of room, vitals, hands, status,
  spell timer, stance
- **Script engine** — full Genie 4 `.cmd` compatibility:
  `#class`, `#alias`, `#var`, `#highlight`, `#trigger`, `#substitute`,
  `#gag`, `#macro` all with save/load + per-character `.cfg` persistence
- **Genie 4 settings importer** (File → Import from Genie 4…)
- **AutoMapper**
  - Click-to-walk with compliance gating
  - Skill-weighted Dijkstra pathfinding (the pathfinder filters out exits
    your character lacks ranks for)
  - Cross-zone routing via `ZoneConnections.xml` (baseline seed shipped;
    community-curated meta-graph)
  - Edit Exit dialog for per-arc skill / RT / wait / notes
- **Dockable panels** via Dock.Avalonia (3-column default layout)
- **Layout save/load** — named workspace presets (Layout menu → Save As…)
- **Script Bar** showing running scripts with stop/edit affordances
- **Tab-complete script names** in command bar
- **Session Recorder** (raw XML capture)
- **Per-tag visibility toggles** — Game / Echo / Script lines
- **Hands strip** with held items, prepared spell, status badges, stance
- **Vitals strip** with health / mana / stamina / spirit / concentration
- **Per-character profiles** with AES-256-GCM password encryption (machine-
  bound key)

## In flight — 🚧

- **Discord community server** — channel layout designed, invite link
  pending publication
- **Mobile-routine workflow polish** — Discord webhook on PR open is
  designed but not wired (see [CONTRIBUTING.md](../CONTRIBUTING.md) for
  the mobile-routine pattern)

## Planned — 🗓

These are scoped well enough that someone could pick one up and ship it
without a deep architecture discussion first.

### LAMP 2.0 — cross-platform updater

Genie 4's LAMP updater is Windows-only. LAMP 2.0 needs to handle three
platforms' app-data conventions:

- Windows: `%APPDATA%/Genie5/`
- macOS: `~/Library/Application Support/Genie5/`
- Linux: XDG `$XDG_DATA_HOME/Genie5/` or `~/.local/share/Genie5/`

`Genie.Core.Runtime.AppPaths` already handles per-platform paths. LAMP 2.0
needs:
- App update channel (signed binaries; semver tracking)
- Maps update channel (pulls from the community Maps repo)
- Plugin update channel (once the plugin host lands)

### Plugin host

Genie 4 supports plugin DLLs implementing a documented C# interface.
Genie 5 currently has **no** plugin host — that's a real gap for users
who depend on community plugins.

Design constraints:
- Plugins are C# DLLs (or eventually `.js` array scripts — separate item)
- Plugins must run in a sandbox that can't violate the DR policy gates
  (no headless mode, no ProcessInput from AI, etc.)
- Plugin discovery from a `Plugins/` folder under the app data root
- API surface should be Genie-4-compatible where possible to ease porting

### JavaScript `.js` array script support

Genie 4 supports `.js` array scripts via a small JavaScript engine. Genie 5
currently runs only `.cmd` scripts. Some community scripts (notably ones
that need real arrays or hash maps beyond what `.cmd` variables provide)
are `.js`-only and don't have `.cmd` equivalents.

Likely implementation: embed a JavaScript runtime (Jint, ClearScript, or
similar) and expose the same `Scripts.Globals` surface that `.cmd` scripts
see.

### Themes / light mode

Currently single-themed dark UI. Avalonia 11 supports theme dictionaries;
the work is mostly designing the light palette and audited contrast levels.

### Visual trigger / flow designer

Pattern-and-action editor for `#trigger` rules that doesn't require typing
regexes by hand. Lower priority but a real onboarding helper.

### `.github/workflows/` CI maturity

Currently has the `build.yml` workflow that runs `dotnet build` on push +
PR. Planned: release workflow that builds self-contained binaries for
win-x64, osx-arm64, osx-x64, linux-x64 and attaches them to GitHub
releases.

## Considering — 💭

These need a design discussion before code lands. Open an issue with the
`design-question` label if you have thoughts.

- **AI-assisted advisor mode** — the AI surface is designed (see
  `AiContextBuffer.cs`) with five release gates (G1–G5). Gates G1, G2,
  G5 need product/UX work. Gate G3 (in-character advisor) stays disabled
  until there's an explicit ToS read.
- **Mudlet plugin / companion library** — `Genie.Core` is deliberately
  UI-free so it could be embedded in another client. Mudlet has a large
  cross-platform user base with no DR-XML support; a Genie.Core-backed
  plugin would bring DR compatibility to Mudlet. Design questions: API
  surface, packaging, who maintains.
- **Container noun map expansion** — the parser already maps `in #NNNN`
  to friendly names like "in My Backpack." Expanding this to all
  containers needs a stable source of truth (probably a community-
  curated noun map XML similar to ZoneConnections).
- **Multi-character at-a-glance dashboard** — explicitly *not* multi-
  character orchestration (that's a hard never per DR policy); a passive
  read-only view of which of your characters are logged in elsewhere, if
  you can plumb it without violating policy.

## Deferred — 🛑 not planned

- **Multi-character orchestration from one client instance** — DR policy
  doesn't allow one tool driving multiple characters.
- **Auto-reconnect with script resume** — see [POLICY.md](POLICY.md).
- **Headless / daemon mode** — see [POLICY.md](POLICY.md).
- **Auto-walk while window unfocused** — see [POLICY.md](POLICY.md).

## How this roadmap gets updated

Roadmap edits land via PRs same as code. If you're starting a 🗓 Planned
item, the same PR that adds the first commit should also move the item to
🚧 In flight. When it ships, move to the "shipped" list.
