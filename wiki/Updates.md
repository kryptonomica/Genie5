# Keeping Up to Date

Genie 5 has a built-in updater so you can keep three things current without hunting for downloads: the **app itself**, your **zone maps**, and your **plugins**. It's an *in-process* update system — distinct from Genie 4's separate `Lamp.exe` updater.

## The Updates dialog

Open it from the **Help** menu (a badge appears there when something is available). It has three tabs:

| Tab | Updates | Source |
| --- | --- | --- |
| **Core** | The Genie 5 application. | GitHub Releases. |
| **Maps** | Community zone maps. | The community Maps repository. |
| **Plugins** | Installed plugins. | Each plugin's configured release feed. |

The updater uses your system's default network settings, so OS-level proxy configuration is honored automatically.

## Core (the app)

Application updates are delivered via [Velopack](https://velopack.io/): the updater fetches the new build from GitHub Releases and applies it in place, so the next launch is the new version. As of **v5.0.0-alpha.3.1** this works on **all three platforms** — Windows, macOS (Apple Silicon + Intel), and Linux (AppImage).

Auto-update requires that you installed via an updater-aware package:

| Platform | Auto-updates if installed via | Update by hand if you used |
| --- | --- | --- |
| Windows | `Genie5-win-Setup.exe` | `Genie5-win-Portable.zip` |
| macOS | `Genie5-osx-Setup.pkg` / `Genie5-osx-x64-Setup.pkg` | the `*-Portable.zip` bundles |
| Linux | `Genie5.AppImage` | — |

The plain **Portable `.zip`** builds don't register for updates — re-download from [Releases](https://github.com/GenieClient/Genie5/releases/latest) to upgrade those. Once you're on an updater-aware install, new releases arrive as small **delta** downloads (only the bytes that changed). See [Installation](Installation) for which download to pick.

> **Signing:** alpha builds are currently unsigned, so first launch shows a SmartScreen / Gatekeeper warning ([details](Installation#platform-first-launch-notes)). A SignPath-backed signing pipeline is in progress (see the [README's code-signing section](https://github.com/GenieClient/Genie5/blob/main/README.md#code-signing-policy)); signed Windows builds are expected from an upcoming release.

> **Release channel (while we're in alpha):** the Core updater has a **stable** / **beta** channel selector in the Updates dialog. Every current build ships as a GitHub **pre-release**, so during the alpha/beta period Genie defaults to the **beta** channel — that's what lets the in-app updater see new alpha builds. If you ever switch to **stable** you'll see "up to date" until the first non-prerelease (5.0.0) ships. Leave it on **beta** to ride the test releases.

## Maps

The Maps tab (equivalently **File → Update Maps from Official Repo…**) pulls the latest zone XML from the community repository and **merges** it with your local data — upstream layout fixes come down while your stamped room ids survive. This has its own page: [Updating Maps & Scripts](Updating-Maps-and-Scripts).

## Plugins

The Plugins tab checks each installed plugin against its configured release feed and offers updates. See [Plugins](Plugins) for installing and managing them.

## Under the hood

The updater is built around small, swappable pieces: a file-list source and a release source (with implementations for GitHub Contents and GitHub Releases), plus per-domain updaters for the app, maps, and plugins. That platform-neutral design is what lets the same dialog drive three very different update flows, and it's what let the macOS and Linux app-update channels slot in (in v5.0.0-alpha.3.1) without reworking the UI.

## What it never does

- **No silent auto-update that resumes your game.** Updating is something you initiate; it doesn't run unattended while you're logged in and playing. This keeps Genie on the right side of [policy](Policy-Compliance) and avoids surprises mid-session.

## Related

- [Installation](Installation) — first install and platform notes.
- [Releases & Changelog](Releases) — what shipped in each build.
- [Updating Maps & Scripts](Updating-Maps-and-Scripts) — the Maps flow in detail.
- [Plugins](Plugins) — managing plugins.
