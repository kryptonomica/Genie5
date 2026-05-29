# Installation

Genie 5 is in **alpha**. Pre-built downloads aren't published yet, so installation currently means building from source — which is a two-command process once you have the .NET 8 SDK. This page covers all three platforms, plus what to expect from the eventual pre-built artifacts.

> **Coming from Genie 4?** Install fresh first, then jump to [Importing Genie 4 Config](Importing-Genie4-Config.md) to bring your aliases, triggers, highlights, etc. across.

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) — Windows, macOS, and Linux builds are all supported by Microsoft.
- [Git](https://git-scm.com/) to clone the repository.

## Build and run (all platforms)

```bash
git clone https://github.com/GenieClient/Genie5.git
cd Genie5
dotnet run --project src/Genie.App
```

That's it — Avalonia is a tier-1 cross-platform UI toolkit, so the same command launches the GUI on Windows, macOS, and Linux.

For a faster-starting build you can compile in Release first:

```bash
dotnet build -c Release
dotnet run -c Release --project src/Genie.App
```

## Producing a standalone executable

To get a single self-contained file you can copy and double-click (no .NET install needed on the target), publish for your platform's runtime identifier:

```bash
# Windows x64
dotnet publish src/Genie.App -c Release -r win-x64   -o publish/win-x64
# macOS Apple Silicon
dotnet publish src/Genie.App -c Release -r osx-arm64 -o publish/osx-arm64
# macOS Intel
dotnet publish src/Genie.App -c Release -r osx-x64   -o publish/osx-x64
# Linux x64
dotnet publish src/Genie.App -c Release -r linux-x64 -o publish/linux-x64
```

The output is a single `Genie` / `Genie.exe`. See [docs/build-and-release.md](../docs/build-and-release.md) for the full publish detail and platform packaging notes.

## Platform first-launch notes

Because alpha builds aren't code-signed, your OS may warn the first time you run a published executable:

### macOS — Gatekeeper

A published `osx-*` build is ad-hoc signed at most, so macOS may refuse the first launch ("developer cannot be verified") or report the app as "damaged":

- **First launch:** in Finder, **right-click** the app → **Open** → **Open**. macOS remembers the choice.
- **"Damaged" error:** a browser download set a quarantine attribute. Strip it:
  ```bash
  xattr -cr /path/to/Genie.app
  ```

(Running via `dotnet run` from source sidesteps Gatekeeper entirely.)

### Windows — SmartScreen

Running an unsigned `.exe` shows a blue "Windows protected your PC" panel: click **More info → Run anyway**. SmartScreen remembers it for that exact file.

### Linux

`dotnet run` works out of the box. A published `linux-x64` binary runs directly; mark it executable if needed (`chmod +x Genie`).

## First launch

On first run Genie 5 creates its per-user data folder (`~/Library/Application Support/Genie5` on macOS, `%APPDATA%\Genie5` on Windows, `~/.local/share/Genie5` on Linux) with `Config/`, `Scripts/`, `Maps/`, and `Logs/` subfolders. See [Application Folders](Application-Folders.md) for the full layout.

## Pre-built artifacts (planned)

Once the release pipeline lands, look for `Genie5-…-{win-x64, osx-arm64, osx-x64, linux-x64}` archives on the [Releases](https://github.com/GenieClient/Genie5/releases) page. Until then, build from source as above.

## After installation

- [Application Folders](Application-Folders.md) — where your data lives on disk.
- [Importing Genie 4 Config](Importing-Genie4-Config.md) — migrate from Genie 4.
- [Updating Maps and Scripts](Updating-Maps-and-Scripts.md) — get the latest community maps.
