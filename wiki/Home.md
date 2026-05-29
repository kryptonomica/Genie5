# Genie 5

Genie 5 is a cross-platform client for [DragonRealms](https://www.play.net/dr), Simutronics' text MMO. It's a from-scratch rewrite of [Genie 4](https://github.com/GenieClient) on .NET 8 and [Avalonia](https://avaloniaui.net/), preserving Genie 4's scripting / mapping / triggering features while running natively on **Windows, macOS, and Linux**.

> ⚠️ **Alpha.** Genie 5 is in active development, targeting parity with the most-used 80% of Genie 4. Expect rough edges. Until pre-built downloads ship, you run it from source — see [Installation](Installation.md).

## New here? Start with

1. **[Installation](Installation.md)** — build and run the client on Windows, macOS, or Linux.
2. **[Application Folders](Application-Folders.md)** — where Genie 5 keeps your scripts, maps, logs, and config.
3. **[Importing from Genie 4](Importing-Genie4-Config.md)** — bring over your aliases, triggers, highlights, etc. from an existing Genie 4 install.
4. **[Updating Maps and Scripts](Updating-Maps-and-Scripts.md)** — pull the latest zone maps from the community repo.

## Connecting

Use **File → Connect…**. Genie 5 connects three ways:

- **Simutronics SGE login** — the standard "log in with your DragonRealms account" flow. Enter your account name and password, click **Fetch** to retrieve your character list, pick a character, and connect. Genie 5 handles SGE auth and finds the right game server. (See the [SGE protocol notes](../docs/SGE_PROTOCOL.md) for the wire-level detail.)
- **Lich proxy** — point Genie at a running [Lich 5](https://github.com/elanthia-online/lich-5) on `127.0.0.1:8000`; your Ruby scripts keep working and Genie sees a clean stream.
- **Dev replay** — replay a recorded raw-XML session through the engine (handy for development).

Saved connections are stored as profiles with the password encrypted on disk (AES-256-GCM). DragonRealms is the supported game; the engine is built around DR's XML protocol.

## Where this wiki fits

| You want… | Look here |
| --- | --- |
| To install, configure, or use Genie 5 | This wiki (you're here). |
| To understand the parser, scripting engine, or mapper internals | The [`docs/` folder](../docs/) in the repository. |
| To file a bug or request a feature | The [Issues tab](https://github.com/GenieClient/Genie5/issues). |
| To contribute code | [CONTRIBUTING.md](../CONTRIBUTING.md). PRs welcome. |

## Community resources

- **Maps repo:** [GenieClient/Maps](https://github.com/GenieClient) — community zone XML for DR, pulled on demand via **File → Update Maps from Official Repo…** (see [Updating Maps and Scripts](Updating-Maps-and-Scripts.md)).
- **Genie 4** — the original client. Its wiki remains the authoritative reference for the scripting language (`put`, `gosub`, `matchwait`, …), since Genie 5 replicates the Genie 4 `.cmd` dialect.
- **Policy compliance** — Genie 5 is designed to live within Simutronics' [Allowed Software policy](https://elanthipedia.play.net/DragonRealms_Policy:_Allowed_Software): no auto-reconnect, attended-mode auto-walk only, advisor-only AI. See the [project README](../README.md#dragonrealms-policy-compliance).
