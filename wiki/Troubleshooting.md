# Troubleshooting & FAQ

Common problems and quick fixes. If none of these help, drop into [Discord](https://discord.gg/MtmzE2w) or [file an issue](https://github.com/GenieClient/Genie5/issues) — a recording (below) makes bug reports far easier to act on.

## Installing & launching

**Which download do I need?**
Pre-built builds ship for all three platforms — see the [Installation](Installation#download-a-pre-built-build-recommended) tables. Short version: Windows → `Genie5-win-Setup.exe`; Linux → `Genie5.AppImage`; macOS → `Genie5-osx-Setup.pkg` (Apple Silicon, M1+) or `Genie5-osx-x64-Setup.pkg` (Intel, pre-2020). Check  → **About This Mac** if you're unsure which Mac you have.

**macOS won't open the app ("developer cannot be verified" / "damaged").**
Alpha builds aren't notarized. Right-click the app → **Open** → **Open** (macOS remembers it). If it reports "damaged," clear the download quarantine: `xattr -d com.apple.quarantine /Applications/Genie5.app` (or `xattr -cr <path>`). Running from source with `dotnet run` avoids this entirely.

**Windows SmartScreen blocks the exe.**
Unsigned alpha builds trigger the blue panel: **More info → Run anyway**. Signed builds (expected in an upcoming release) won't. See [Installation](Installation).

**Linux: AppImage won't run / "FUSE not installed."**
Make it executable first (`chmod +x Genie5.AppImage`), then `./Genie5.AppImage`. For the FUSE error, install it — Debian/Ubuntu: `sudo apt install libfuse2` (Fedora usually works out of the box). If text renders oddly on a minimal distro, install `fontconfig`. For a desktop-menu entry, use [AppImageLauncher](https://github.com/TheAssassin/AppImageLauncher).

**`dotnet` isn't recognized / build fails (building from source).**
You need the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (the SDK, not just the runtime). Verify with `dotnet --version` (should be 8.x). See [Installation](Installation#build-from-source).

## Connecting

**Fetch returns no characters.**
Usually a wrong account name or password, or the account has no DragonRealms characters. Re-enter carefully (the password field is case-sensitive).

**"Already logged in."**
That character is still in game in another session. Quit it there, then reconnect.

**"Billing problem."**
The account's subscription needs attention on [play.net](https://www.play.net/dr).

**Connect hangs, then fails.**
A firewall/proxy is blocking `play.net`, or the game is down for maintenance. Genie retries the **initial** connect a few times, then stops (it never auto-reconnects after a session was established — see [Policy Compliance](Policy-Compliance)).

**Connecting through Lich.**
Start Lich first so it's listening (default `127.0.0.1:8000`), then choose **Lich Proxy** in the Connect dialog. See [Lich 5 Integration](Lich-5-Integration).

More on connection messages: [Connecting & Profiles](Connecting#connection-troubleshooting).

## Files & profiles

**Where are my scripts/maps/settings?**
In your per-user Genie 5 folder — `%APPDATA%\Genie5` (Windows), `~/Library/Application Support/Genie5` (macOS), `~/.local/share/Genie5` (Linux). Full map: [Application Folders](Application-Folders).

**I saved a profile but it didn't keep my password.**
Saving the password is optional. If you saved without it, you'll be prompted at connect. When saved, it's encrypted with AES-256-GCM — never plain text.

**My settings/scripts from Genie 4 aren't here.**
Import rules via **File → Import from Genie 4…** ([guide](Importing-Genie4-Config)); copy `.cmd` scripts into your [Scripts folder](Application-Folders); import maps via the mapper ([guide](Updating-Maps-and-Scripts)).

## Scripts

**A script stops with "undefined variable."**
By design — Genie 5 aborts rather than silently expanding an undefined `$var` to empty (a common Genie 4 bug). Guard with `if def(name)`. See [Scripting](Scripting#whats-different-from-genie-4).

**A Genie 4 script doesn't behave the same.**
Script-compat regressions are treated as bugs — please [file an issue](https://github.com/GenieClient/Genie5/issues/new) with the script (or the failing lines). A few intentional differences are listed in [Scripting Reference](Scripting-Reference#differences-from-genie-4).

**`#scripts`, `#stop`, `#stopall`** list and stop running scripts; the Script Bar shows them with controls.

## Mapper

**The map doesn't match where I am.**
Turn on the AutoMapper (learning) toggle and walk through the area so rooms get stamped, or pull a fresh copy of the zone via **File → Update Maps from Official Repo…**. Dense cities with duplicate room titles can briefly fail to lock on — Genie declines rather than guessing wrong. See [The Mapper](Mapper).

**Click-to-walk stopped partway.**
Expected if you typed a command, pressed Esc, the window lost focus for ~60s, or you got knocked off the route — the walker cancels rather than firing the wrong command. Click again to restart. See [Policy Compliance](Policy-Compliance).

**Cross-zone routes don't walk yet.**
Single-zone walking works; feeding full cross-zone routes to the walker is still being finished. See [Cross-Zone Travel](Cross-Zone-Travel).

## Updating

**Every file fails during a maps update.**
No network, a proxy/firewall blocking the repo host, or a badly-skewed system clock (which breaks TLS). Local maps are never corrupted by a failed update — failed files are skipped; re-run later. See [Updating Maps & Scripts](Updating-Maps-and-Scripts#troubleshooting).

**The app updater didn't offer an update.**
In-app **Core** updates work on all three platforms as of v5.0.0-alpha.3.1, but only if you installed an updater-aware package: `Genie5-win-Setup.exe` (Windows), a `.pkg` (macOS), or the `Genie5.AppImage` (Linux). The plain **Portable `.zip`** builds don't register for updates — re-download from [Releases](https://github.com/GenieClient/Genie5/releases/latest) to upgrade those. See [Keeping Up to Date](Updates#core-the-app).

## Resetting to a clean state

To check whether a problem reproduces on a fresh install (and to recover safely):

1. **Quit Genie 5.**
2. **Rename** (don't delete) your `Genie5/` data folder to `Genie5-old/`.
3. Launch — a fresh folder is created.
4. Copy specific files back from `Genie5-old/` while the app is closed.

Details: [Application Folders](Application-Folders#resetting-to-defaults).

## Reporting a bug well

1. **File → Record Session** (toggle on — title bar shows 🔴 REC).
2. Reproduce the problem.
3. Disconnect (recording auto-stops); find the file in your `Logs/` folder.
4. [Open an issue](https://github.com/GenieClient/Genie5/issues/new) describing what you did, what you expected, and what happened — attach the relevant snippet. Include your OS and `dotnet --version`.

Recordings stay on your machine until you choose to share a snippet.

## Related

- [Connecting & Profiles](Connecting) · [Application Folders](Application-Folders) · [The Mapper](Mapper) · [Scripting](Scripting) · [Policy Compliance](Policy-Compliance)
