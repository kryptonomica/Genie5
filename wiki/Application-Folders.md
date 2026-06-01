# Application Folders

Genie 5 keeps all of your personal data — scripts, maps, logs, settings, per-character profiles — in a single per-user folder. Knowing where it lives is useful for backing up before an upgrade, editing scripts in your own editor, pointing the import dialog at a Genie 4 install, and syncing across machines.

## Where Genie 5 lives

The location is resolved by [AppPaths.Discover](https://github.com/GenieClient/Genie5/blob/main/src/Genie.Core/Runtime/AppPaths.cs):

### macOS

```
~/Library/Application Support/Genie5/
```

Paste that into Finder's **Go → Go to Folder…** (`⇧⌘G`). `Library` is hidden by default; the Go-to dialog reveals it.

### Windows

```
%APPDATA%\Genie5\
```

…which resolves to `C:\Users\<you>\AppData\Roaming\Genie5\`. Paste `%APPDATA%\Genie5` into Explorer's address bar.

### Linux

```
$XDG_DATA_HOME/Genie5/      # or, if XDG_DATA_HOME is unset:
~/.local/share/Genie5/
```

### Portable mode

If a folder named `Config` exists **next to the Genie executable**, Genie 5 runs in *portable* mode and keeps everything alongside the app instead of in the per-user location. Useful for a USB-stick or self-contained install. (This is the first thing `AppPaths.Discover` checks.)

## What's inside

```
Genie5/
├── Config/      ← settings.cfg + rule .cfg files (shared / non-character)
├── Profiles/    ← per-character config: Profiles/<Char>-<Account>/*.cfg
├── Scripts/     ← your .cmd files
├── Maps/        ← zone files (Map##_*.xml) + ZoneConnections.xml
├── Logs/        ← AutoLog output, one file per character per session
├── Sounds/      ← sound files for #play / triggers
├── Plugins/     ← plugin host (roadmap)
└── Art/         ← image assets
```

| Folder | What's in it | When to touch |
| --- | --- | --- |
| `Config/` | `settings.cfg` (app settings) and the shared rule files: `aliases.cfg`, `triggers.cfg`, `highlights.cfg`, `substitutes.cfg`, `gags.cfg`, `macros.cfg`, `variables.cfg`, `classes.cfg`. Each is a plain-text list of the commands that recreate the rules. | Mostly managed via **Edit → Configuration…**. Hand-editable — Genie 5 reloads on next launch. |
| `Profiles/` | One subfolder per character (`<Char>-<Account>/`) holding that character's own copy of the rule `.cfg` files. The first time a character connects, the folder is seeded from your shared `Config/` files, then diverges independently. | Created automatically. Edit the per-character files here, or via the GUI while that character is connected. |
| `Scripts/` | Your `.cmd` script files, plus any helper scripts you pull from the community repo. | Drop any script here to run it as `.scriptname` (or `put .scriptname`). |
| `Maps/` | Zone files in Genie 4's XML format (`Map1_Crossing.xml`, …) and `ZoneConnections.xml` (the cross-zone transit graph). | Populated via **File → Import from Genie 4…** or **File → Update Maps from Official Repo…**. Jump there via **File → Open Maps Folder**. |
| `Logs/` | When AutoLog is on, each session writes a `<character>_<timestamp>` log of plain in/out text. | Read-only from the app's view. Safe to delete or archive. |

> **Note on formats.** Genie 5 stores rule config as Genie 4-style `.cfg` files (one command per line) and zone maps as Genie 4-style XML — not JSON. This keeps round-tripping with the Genie 4 ecosystem and the community Maps repo clean.

## Per-character profiles

Settings split into two tiers:

- **Shared** (`Config/`) — the baseline, used by sessions without a character (LIST mode, dev replay).
- **Per-character** (`Profiles/<Char>-<Account>/`) — each character gets its own aliases/triggers/etc., seeded once from the shared baseline. So your combat triggers on one character don't follow you onto a shopping alt.

The active profile directory is chosen at connect time from the character + account names.

## Backups, syncing, multiple machines

Everything in `Genie5/` is plain text (`.cfg` / `.cmd` / `.xml` / `.log`). Copy the whole folder to a backup drive or sync it via Dropbox / iCloud / OneDrive; Genie 5 picks it up on the destination machine next launch. To share GUI rules but keep scripts machine-local, sync only `Config/` and `Profiles/`.

## Resetting to defaults

1. **Quit Genie 5.**
2. **Rename** (don't delete) the `Genie5/` folder to `Genie5-old/`.
3. Launch — a fresh empty `Genie5/` is created.
4. Recover specific files later by copying them back from `Genie5-old/` while the app is closed.

This is also the clean way to check "does my bug reproduce on a fresh install?" before reporting it.

## Why this location?

`~/Library/Application Support` (macOS), `%APPDATA%` (Windows), and `$XDG_DATA_HOME` (Linux) are the standard per-user "non-document" data locations. They survive app reinstalls, get backed up by Time Machine / OneDrive, don't clutter your home directory, and aren't shared between OS users. The folder is created on first launch — nothing to set up in advance.
