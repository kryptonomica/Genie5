# Genie 5 Developer Documentation

Genie 5 is a cross-platform Avalonia / .NET 8 rebuild of [Genie 4](https://github.com/GenieClient) — a long-running client for [DragonRealms](https://www.play.net/dr), Simutronics' text MMO. These docs cover the internals: how the client speaks to the server, how a line of game text turns into UI/scripts/state, how scripts execute, and how the mapper finds and walks paths.

The codebase splits into two projects:

- **`Genie.Core`** — pure engine library, zero UI dependencies. Connection, protocol parsing, game-state, the script engine, rule engines, the mapper, and the AI pipe. (Builds as an exe only so the headless [TestHarness](../src/Genie.Core/TestHarness.cs) can run.)
- **`Genie.App`** — the Avalonia GUI host. Binds to `Genie.Core` observables and owns no game-logic state.

If you're new to the codebase, read in this order: **[SGE Protocol](SGE_PROTOCOL.md)** → **[DR XML Protocol](dr-xml-protocol.md)** → **[Line Pipeline](line-pipeline.md)**. Almost every quirk elsewhere traces back to how the server speaks and how a line flows through the engine.

## Table of contents

| Topic | Status | Summary |
| --- | --- | --- |
| [SGE Protocol](SGE_PROTOCOL.md) | ✅ | The Simutronics login flow at `eaccess.play.net:7900` — how [SgeAuthClient](../src/Genie.Core/SgeAuthClient.cs) authenticates and gets a game host/port/key. |
| [DR XML Protocol](dr-xml-protocol.md) | ✅ | The DragonRealms XML-ish stream, its tag vocabulary, and how [DrXmlParser](../src/Genie.Core/DrXmlParser.cs) turns it into typed `GameEvent`s. |
| [Line Pipeline](line-pipeline.md) | ✅ | How one `GameEvent` fans out: [GenieCore](../src/Genie.Core/GenieCore.cs) → state / scripts / triggers / globals, and the UI render path in [GameTextViewModel](../src/Genie.App/ViewModels/GameTextViewModel.cs). |
| [Scripting Engine](scripting-engine.md) | ✅ | [ScriptEngine](../src/Genie.Core/Scripting/ScriptEngine.cs): tick loop, type-ahead, match/wait/pause semantics, the roundtime gate, and the `%`/`$` variable scopes. |
| [Mapper](mapper.md) | ✅ | [AutoMapperEngine](../src/Genie.Core/Mapper/AutoMapperEngine.cs) + [AutoWalkService](../src/Genie.App/Services/AutoWalkService.cs): node resolution, in-zone pathfinding, and the attended-mode walker. Companion to [AUTOMAPPER_DESIGN.md](AUTOMAPPER_DESIGN.md). |
| [Multi-Zone Travel](multi-zone-travel.md) | 🚧 partial | [MultiZonePathfinder](../src/Genie.Core/Mapper/MultiZonePathfinder.cs) + [ZoneConnection](../src/Genie.Core/Mapper/ZoneConnection.cs): cross-zone routing over the community `ZoneConnections.xml` graph. |
| [Build & Release](build-and-release.md) | ✅ | `dotnet publish` per-platform, the single-file self-contained settings, and version stamping. |
| [Genie 4 vs Genie 5](GENIE4_VS_GENIE5.md) | ✅ | Comprehensive feature-by-feature comparison against the original Windows client. |
| [AutoMapper Design](AUTOMAPPER_DESIGN.md) | 🚧 | The full auto-walk design proposal: skill-weighted paths, cross-zone travel, user-editable connection database. |

End-user documentation (install, folders, importing, updating maps) lives in the [`wiki/`](../wiki/) folder.

## Conventions

- **File references** use repo-relative paths and link to the source (e.g. [DrXmlParser.cs](../src/Genie.Core/DrXmlParser.cs)). Line-specific references look like `DrXmlParser.cs:184`.
- **Genie 4 references** point at the original client; it is the authoritative reference for behaviour we replicate (especially the `.cmd` script dialect and `.cfg` formats).
- **Load-bearing comments.** The source carries long "why" comments next to tricky logic. These pages summarise and link rather than restate — when in doubt, the comment in the code wins.

## Contributing

When you change a documented subsystem, update the corresponding page in the same PR. The docs live in-tree so they're reviewed alongside the code change. See [CONTRIBUTING.md](../CONTRIBUTING.md).
