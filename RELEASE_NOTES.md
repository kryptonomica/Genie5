# Genie 5 — v5.0.0-alpha.5

A feature batch on top of alpha.4.2: in-window prompt rendering, room/scene
artwork, preset colouring, a full reserved-variable vocabulary for scripting,
and a round of Genie 4 parity in the mapper and `#config` system.

> **Alpha software.** Expect rough edges. Builds are **unsigned** — Windows
> SmartScreen will warn on first launch (More info → Run anyway). Signing is
> tracked in #33.

## ✨ New since alpha.4

- **Game prompt in the window** — the `>` / `R>` / `H>` prompt now renders in
  the game window using your `prompt` string. `promptbreak` controls whether it
  gets its own line; `promptforce` reconstructs the status letters (kneeling,
  hidden, roundtime, …) from live indicators.
- **Scene panel** (`#config showimages`) — DR room/scene artwork, fetched from
  play.net's art CDN and shown in a dockable panel (Window → Scene).
- **Preset colouring** — room descriptions, whispers, speech and the rest of the
  preset palette now render in their configured colours, with a
  Configuration → Presets editor.
- **`#config` settings system** — `#config <key> <value>` / `<key>` / `list`,
  backed by `settings.cfg`, with ~20 Genie 4 settings wired and a
  Configuration → Scripts tab.
- **Scripting variables** — the full reserved vocabulary is exposed
  (`$health`, `$roomid`, `$zoneid`, status flags, hands, the clock family, …),
  and `#var` now lists the reserved/live-state set alongside your own variables.
- **Mapper** — `#mapper reset` re-resolves a lost location without moving;
  multi-level view shows ghost rooms one level above/below (`automapperalpha`).
- **Sound** — SFX on triggers/highlights and a `#play` command (cross-platform).
- **Quality of life** — Condensed mode (collapse blank lines), AutoLog
  (automatic session log), spell timer (`$spelltime`), monster count
  (`$monstercount` / `$monsterlist`), portable-first storage with a first-run
  location prompt, and a Help → About dialog.

## ✅ What works

Connection (Direct SGE / Lich proxy / dev-replay), the StormFront XML parser and
live GameState, the full Genie 4 `.cmd` script engine plus JavaScript `.js`
scripts, the rules engines (`#alias` / `#trigger` / `#highlight` / `#substitute`
/ `#gag` / `#macro` / `#class` / `#var`) with `.cfg` persistence, the AutoMapper
(click-to-goto, `#goto`, cross-zone routing), dockable panels with save/load
layouts, the plugin host, and the in-app updater. See the
[README status table](README.md#status) for the full list.

## 🚧 Not working yet / known gaps

- **Unsigned builds** — SmartScreen warning on Windows (#33).
- **macOS / Linux update channels** — the in-app updater self-updates on Windows
  only; other platforms install fresh builds manually for now (#27).
- **Mapper pathing** — some same-description rooms can still mis-resolve or stall
  a path (#76 / #77); `#mapper reset` helps.
- **No light theme** yet (single dark palette, #20); no injuries panel (#18);
  no Familiar/Death/Assess stream tabs (#17); no raw-XML inspector window (#14).

## ⬇️ Downloads

Grab the installer or portable build for your platform from the assets below:

| Platform | Installer | Portable |
|---|---|---|
| Windows | `01-Windows-Genie5-Setup.exe` | `01-Windows-Genie5-Portable.zip` |
| macOS (Apple Silicon) | `02-macOS-Apple-Silicon-Genie5.dmg` | `02-…-Portable.zip` |
| macOS (Intel) | `03-macOS-Intel-Genie5.dmg` | `03-…-Portable.zip` |
| Linux (x64) | `04-Linux-Genie5.AppImage` | — |

**Full changelog:** https://github.com/GenieClient/Genie5/compare/v5.0.0-alpha.4.2...v5.0.0-alpha.5
