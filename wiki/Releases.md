# Releases & Changelog

Where to get Genie 5 and what changed in each build. Downloads live on the [Releases page](https://github.com/GenieClient/Genie5/releases); the [latest release](https://github.com/GenieClient/Genie5/releases/latest) is always the one to grab. For how to install each download, see [Installation](Installation); for staying current after that, [Keeping Up to Date](Updates).

> Genie 5 is **alpha**. Versions are tagged `v5.0.0-alpha.N`. Builds are unsigned for now (Windows/macOS show a first-launch warning — see [Installation](Installation#platform-first-launch-notes)); signed Windows builds are expected from an upcoming release.

## Latest: v5.0.0-alpha.3.1 — cross-platform companion (Linux + macOS)

The headline: **Genie 5 now has native downloads for all three platforms.** This is the cross-platform companion to alpha.3 — the **same codebase**, plus the Linux and macOS binaries that weren't ready when alpha.3 first shipped. It adds *platforms*, not features.

**First-ever native Genie client on Linux and macOS:**

- 🐧 **Linux x64** — `Genie5.AppImage`, a single-file executable for Ubuntu / Fedora / Debian / Arch / etc.
- 🍎 **macOS Apple Silicon (M1+)** — `Genie5-osx-Setup.pkg` installer, or `Genie5-osx-Portable.zip`.
- 🍎 **macOS Intel (pre-2020)** — `Genie5-osx-x64-Setup.pkg` / `Genie5-osx-x64-Portable.zip`.

On each of these platforms the **in-app updater** handles subsequent releases, just like Windows. Pick your download in the [Installation](Installation#download-a-pre-built-build-recommended) tables.

**Already on Windows alpha.3?** This is offered to you as a tiny delta through the in-app updater — nothing changes behaviourally.

**Read before you run:**

- **macOS** — unsigned, so Gatekeeper blocks the first launch. Right-click → **Open**, or `xattr -d com.apple.quarantine <path>`. ([details](Installation#macos--gatekeeper))
- **Linux** — `chmod +x Genie5.AppImage` first; install FUSE (`sudo apt install libfuse2`) if you see a FUSE error; minimal distros may need `fontconfig`. ([details](Installation#-linux))
- **Windows** — unchanged from alpha.3; SmartScreen still warns until builds are signed (code-signing is planned for an upcoming release).

> ⚠️ Linux and macOS are **brand-new, alpha-tier platforms** here. CI builds them cleanly, but no live-app smoke test has happened on either OS yet. First-tester reports — what works, what's broken, what renders with a weird font — are very welcome: [file an issue](https://github.com/GenieClient/Genie5/issues/new) or post in [Discord](https://discord.gg/MtmzE2w).

[Full release notes →](https://github.com/GenieClient/Genie5/releases/tag/v5.0.0-alpha.3.1)

## v5.0.0-alpha.3 — Integrated updater

The release that made Genie 5 **self-updating** — no more downloading a fresh zip every version.

- **Integrated updater** — one **Help → Check for Updates** dialog with three channels: **Core** (the app, via Velopack binary-diff updates that install and restart from inside the app), **Maps** (zone XML from the community repo), and **Plugins** (DLLs from configured feeds, with a new `#plugin` command to inspect / install / remove). The Help menu shows a badge when an update is available. It's an in-process system — distinct from Genie 4's separate `Lamp.exe`. See [Keeping Up to Date](Updates).
- **Windows installer** — `Genie5-win-Setup.exe` registers the app for auto-updates; from an alpha.3 install onward, new releases arrive in-app.
- **Code-signing pipeline (in progress)** — a tag-triggered workflow that submits the Windows build to the [SignPath Foundation](https://signpath.org/) for approval and signing. This build is still unsigned; code-signing is expected to land in an upcoming release.

[Full release notes →](https://github.com/GenieClient/Genie5/releases/tag/v5.0.0-alpha.3)

## Earlier milestones

These predate the current download/updater setup but mark where major subsystems landed:

- **alpha.2** — **Plugin system** shipped: load plugins from `Plugins/` with no rebuild, per-plugin isolation, the `#plugin` command, and the Experience-tracker example. See [Plugins](Plugins).
- **alpha.1** — first public alpha: SGE + Lich + replay connections, the StormFront XML parser, the GameState engine, the full Genie 4 `.cmd` [script engine](Scripting) and all the [rule engines](Configuration), the [mapper](Mapper) with click-to-walk, per-character encrypted [profiles](Connecting), and dockable [panels](The-Interface).

## Roadmap

For what's planned versus shipped, see the [project roadmap](https://github.com/GenieClient/Genie5/blob/main/docs/ROADMAP.md). Wiki pages flag still-unshipped features with 🚧.

## Related

- [Installation](Installation) — pick and install the right download.
- [Keeping Up to Date](Updates) — the in-app updater.
- [Troubleshooting & FAQ](Troubleshooting) — first-launch and platform gotchas.
