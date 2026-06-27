# Text-to-Speech

Genie 5 can read the game aloud using natural neural voices that run **entirely
on your computer** — offline, free, and private. No game text ever leaves your
machine. It's built for blind and low-vision players, and useful for anyone who'd
rather listen than watch the screen.

Everything is **opt-in**: nothing is spoken until you turn it on.

## Quick start

```
#tts install          install the default voice (a one-time ~60 MB download)
#speak hello there     read a line aloud
#tts read on           auto-read whispers, talk, thoughts, and deaths
```

## Installing voices

Voices are downloaded on demand and stored under your data folder's `Voices`
directory (see [Application Folders](Application-Folders)).

| Command | What it does |
|---|---|
| `#tts install` | Download and select the default voice |
| `#tts install <name>` | Download a specific voice (e.g. `#tts install lessac`) |
| `#tts voices` | List available voices and which are installed |
| `#tts use <name>` | Switch the active voice |
| `#tts status` | Show the voice folder, installed voices, and read-aloud state |

Voices work identically on Windows, macOS, and Linux.

## Speaking text

- **`#speak <text>`** (alias **`#say`**) — read a line aloud. Works typed, from
  scripts, and as a trigger action.
- **`#tts stop`** — silence the current line and clear anything queued.

## Reading streams aloud

Turn on per-stream read-aloud to have Genie announce game text automatically.

| Command | What it does |
|---|---|
| `#tts read` | Show whether read-aloud is on and which streams are read |
| `#tts read on` / `#tts read off` | Master switch |
| `#tts read <stream>` | Add a stream (e.g. `#tts read combat`) and turn read-aloud on |
| `#tts mute <stream>` | Stop reading a stream |

Default streams are **whispers, talk, thoughts, deaths** — the "someone's
talking to me / something important happened" set. The chatty streams (combat,
atmospherics) and `main` are left off by default; add them if you want them.

Urgent lines (whispers, deaths) are spoken first and can interrupt ongoing
chatter, so you never miss a tell behind a wall of room text.

## Settings

These persist with your profile (see [Configuration & Rules](Configuration)):

| Setting | Meaning |
|---|---|
| `ttsvoicedir` | Folder holding installed voices (default `Voices`) |
| `ttsvoice` | Selected voice folder name (set by `#tts use`) |
| `ttsread` | Master read-aloud on/off |
| `ttsreadstreams` | Comma-separated streams to read aloud |

## Coming next

Content-aware grouping (merging *"X arrived" + "X left"* into one short line),
per-trigger and per-highlight speak flags, and a travel mode that announces your
journey and stays quiet in between.
