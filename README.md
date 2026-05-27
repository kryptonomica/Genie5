# Genie 5

**A cross-platform, modern successor to [Genie 4](https://github.com/GenieClient) — the long-running Windows client for [DragonRealms](https://www.play.net/dr), Simutronics' text-based MMO.**

> ⚠️ **Alpha** — Genie 5 is in active development. The first public alpha targets feature parity with the most-used 80% of Genie 4 while running natively on Windows, macOS, and Linux. Expect rough edges. File issues; PRs welcome.

## Why Genie 5

The Genie 4 codebase is WinForms + Windows-only and hasn't kept pace with modern .NET, cross-platform tooling, or the broader scripting ecosystem. Genie 5 is a clean rewrite that:

- **Runs everywhere** — Windows, macOS, and Linux native, courtesy of [Avalonia UI](https://avaloniaui.net/) and .NET 8
- **Stays compatible** — runs your existing Genie 4 `.cmd` scripts, profile files, and `.map` zone data
- **Plays well with the ecosystem** — supports direct SGE auth, [Lich 5](https://github.com/elanthia-online/lich-5) proxy, and dev-replay from recorded sessions
- **Is built for inspection** — clean `Genie.Core` library with no UI dependencies; embed it in other clients, plugins, or test harnesses

## Status

| Layer | State |
|---|---|
| SGE authentication (StormFront + Wizard modes) | ✅ Working |
| DragonRealms XML parser (`<component>`, `<d>`, `<a href>`, `<container>`, `<roundTime>`, etc.) | ✅ Working |
| Live game session — connect, play, type commands | ✅ Working |
| Genie 4 `.cmd` script engine (labels, `MATCH`, `GOSUB`, `$variables`, `WAITFOR`, etc.) | ✅ Working |
| Rules engines (`#alias`, `#trigger`, `#highlight`, `#substitute`, `#gag`, `#macro`, `#class`, `#var`) | ✅ Working with `.cfg` persistence |
| Per-character profile storage with AES-GCM password encryption | ✅ Working |
| Dockable UI panels (vitals, room, inventory, mapper, stream tabs) | ✅ Working |
| Mapper with click-to-goto, zone fingerprinting, Less Obvious Paths | ✅ Working (auto-walk is a roadmap item) |
| Session Recorder for raw-XML capture | ✅ Working |
| Lich 5 proxy mode (`ConnectionMode.LichProxy`) | ✅ Working |
| Dev-replay mode (replay recorded sessions through the engine) | ✅ Working (via Console) |
| LAMP 2.0 cross-platform updater | 🚧 Roadmap |
| Plugin host + marketplace | 🚧 Roadmap |
| JavaScript script support (`.js` array scripts) | 🚧 Roadmap |
| Visual trigger / flow designer | 🚧 Roadmap |
| AI-assisted automation (advisor-only mode) | 🚧 Roadmap |

See [backlog](docs/ROADMAP.md) for the full feature roadmap.

## Installation

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (until pre-built alpha artefacts ship)

### Build from source

```sh
git clone https://github.com/GenieClient/Genie5.git
cd Genie5
dotnet build -c Release
dotnet run --project src/Genie.App
```

On first launch Genie 5 will:

1. Migrate any existing Genie 4 scripts from `%USERPROFILE%\Documents\Genie 4\Scripts\` into its own `Scripts/` folder (Windows only — no-op elsewhere)
2. Migrate any existing Genie 4 maps from `%USERPROFILE%\Documents\Genie 4\Maps\` (Windows only)
3. Create `{AppData}/Genie5/Config/` for `.cfg` rule files and per-character profile data

### Pre-built artefacts

Once the release pipeline lands, look for `Genie5-v5.0.0-alpha.1-{win-x64, osx-arm64, osx-x64, linux-x64}` on the [Releases](https://github.com/GenieClient/Genie5/releases) page.

## Quick start

1. **Launch Genie 5** and use **File → Connect…**
2. **Enter your DragonRealms account name + password**, then click **Fetch** to retrieve your character list
3. **Pick a character** and click **Connect**
4. **Type commands** in the input bar at the bottom; click `<d>` links in game text to send the underlying command
5. **Save the connection** as a profile so you don't have to retype next time — passwords are encrypted on disk (AES-256-GCM)

### Running your first script

Genie 4 `.cmd` scripts go in `{AppData}/Genie5/Scripts/`. From the game window:

```
.myscript           # runs Scripts/myscript.cmd
.myscript arg1 arg2 # passes %1 = arg1, %2 = arg2
#scripts            # lists running scripts
#stop myscript      # aborts a running script
```

The script engine is a faithful port of Genie 4's Wizard-derived dialect — labels, `GOSUB`/`RETURN`, `MATCH`/`MATCHRE`, `$variables`, `%variables`, `PAUSE`, `WAIT`, `WAITFOR`, `PUT`, `def()`, the whole vocabulary. If a script worked in Genie 4 and doesn't work here, [file an issue](https://github.com/GenieClient/Genie5/issues/new) — we treat script-compat regressions as bugs.

## Lich 5 interoperability

Genie 5 plays nicely with [Lich 5](https://github.com/elanthia-online/lich-5). Two integration paths:

- **Lich proxy mode** — Lich runs as your auth front-end and forwards a clean DR stream to Genie on `127.0.0.1:8000`. Pick `Lich Proxy` in the Connect dialog. Your Lich Ruby scripts continue to work; Genie sees them as ordinary game output.
- **Direct SGE + Lich passive** — Genie handles auth itself (no Lich required), and you can run any Lich-managed automation in parallel using Lich's own command channel.

## DragonRealms policy compliance

Genie 5 is designed to live well within Simutronics' [Allowed Software policy](https://elanthipedia.play.net/DragonRealms_Policy:_Allowed_Software). A few things you'll **not** find here, on purpose:

- **No auto-reconnect.** If you disconnect, you reconnect by hand.
- **No agentive AI mode.** AI features (when they ship) are **advisor-only** — they surface suggestions in a panel you read, never drive game commands directly.
- **No auto-walk while window unfocused.** Movement scripts pause if Genie isn't the foreground window.
- **No headless mode.** Genie is a UI client, not a background service.
- **No shipping other players' speech to external services.** The AI context buffer filters out whisper / talk / thoughts / familiar / tells before any external API call.

See [docs/POLICY.md](docs/POLICY.md) for the full compliance review.

## Architecture

```
TCP bytes
  └─► GameConnection          (SgeAuthClient or LichProxy)
        └─► RawXmlStream      (hot IObservable<string>, always on)
              ├─► DrXmlParser.Feed()
              │     └─► GameEvents  (typed records: TextEvent, NavEvent, …)
              │               └─► GameStateEngine → GameState (live snapshot)
              └─► AiRawStream (toggleable, never blocks parser)
                    └─► AiContextBuffer → AI vendor API → AnalysisReady event
```

- **Genie.Core** — pure class library, zero UI dependencies. Connection, parser, game state, script engine, AI pipeline, rules engines.
- **Genie.App** — the Avalonia GUI host; binds to Core observables, owns no game-logic state.

## Reference

| Resource | Why it's relevant |
|---|---|
| [Genie 4 source](https://github.com/GenieClient) | Original Windows client; canonical reference for `.cmd` parity |
| [Lich 5](https://github.com/elanthia-online/lich-5) | Ruby proxy that runs *on top of* Genie, not a competitor |
| [DR-Genie-Scripts](https://github.com/Tirost/DR-Genie-Scripts) | Largest community script collection; our compatibility test set |
| [Elanthipedia](https://elanthipedia.play.net/Main_Page) | DR's community wiki |
| [Mudlet](https://www.mudlet.org/) | Cross-platform MUD client; possible expansion target for SGE/DR support |

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). Bug reports, feature requests, and PRs all welcome. Security issues go through the process in [SECURITY.md](SECURITY.md).

## Community

- **Discord** — [link TBD; coordinate with existing Genie / DR scripting channels]
- **Issues** — [GitHub Issues](https://github.com/GenieClient/Genie5/issues)
- **Discussions** — [GitHub Discussions](https://github.com/GenieClient/Genie5/discussions) (TBD)

## License

[GPL-3.0](LICENSE). Same license as Lich 5, aligning Genie 5 with the broader DR-tooling ecosystem.
