# The Interface

Genie 5's window is a set of **dockable panels** around a central game-text view. You can show, hide, drag, float, and re-dock any panel, then save the arrangement as a layout. This page is a tour of what's on screen and how to rearrange it.

## The default layout

Out of the box you get a three-column arrangement:

- **Left column** — the **Room** panel (title, description, obvious paths) and a cluster of **stream tabs** (Talk, Whispers, Thoughts, Combat, Logons).
- **Center column** — the main **Game** text window, with the **Mapper** below it.
- **Right column** — your **Inventory / Backpack**.
- **Bottom strips** — **vitals**, the **hands** strip, and the **command bar**.

## The panels

| Panel | Shows |
| --- | --- |
| **Game** | The main scrolling game text. Supports clickable links and per-tag visibility (below). |
| **Room** | Current room title, description, and obvious paths — split out from the game text so it's always glanceable. |
| **Inventory / Backpack** | What you're carrying. |
| **Mapper** | The zone map with your current room highlighted; click a room to walk there. See [The Mapper](Mapper). |
| **Stream tabs** | DragonRealms routes certain text to named streams. Genie surfaces **Talk**, **Whispers**, **Thoughts**, **Combat**, and **Logons** as their own tabs so they each keep a clean scrollback. (Speech and whispers also appear in the main window — DragonRealms sends them to both by design.) |

Toggle any panel from the **Window** menu. Drag a panel's tab to re-dock it; drag it out to **float** it in its own window, and drag it back to re-dock.

## The bottom strips

- **Vitals** — bars for **health, mana, stamina, spirit, and concentration**, plus status badges (kneeling, prone, stunned, hidden, bleeding, …).
- **Hands strip** — what's in your **left** and **right** hands, your **prepared spell** (with a cast-time bar), and your **stance**. Its position (top or bottom) is configurable from the **Window** menu.
- **Command bar** — where you type. A **roundtime** indicator shows here (or on the hands strip — your choice) so you can see when you can act again.

## The command bar

Type a game command and press **Enter**. Beyond plain commands:

- **Command history** — **↑ / ↓** recall previous input.
- **Meta-commands** start with `#` (for example `#alias`, `#trigger`, `#var`, `#scripts`). These configure Genie rather than going to the game — see [Configuration & Rules](Configuration).
- **Scripts** start with `.` — `.hunt` runs `Scripts/hunt.cmd`. See [Scripting](Scripting).
- **Tab-completion** — start typing a `.scriptname` and press **Tab** to complete it.
- **Multiple commands** — chain with `;` (the separator character).

## Clickable links

DragonRealms marks many nouns, menu options, and URLs as links:

- **Command links** (`<d>`) — click to send the underlying game command (Genie shows a friendly label where the game provides one).
- **URL links** (`<a href>`) — open in your browser. Genie can confirm before opening external URLs as a safety check.

## Per-tag visibility

The **Window → Game Window** submenu lets you independently show or hide three kinds of lines in the main window:

- **Game text** — what the server sends.
- **Echo** — Genie's own confirmations.
- **Script lines** — output from running scripts.

Turn off script lines, for example, to keep a busy hunt script from cluttering the main window (its output still appears in the Scripts panel).

## Layouts

Rearrange the panels however you like, then keep the arrangement:

- **Window → Reset Layout** restores the default three-column layout.
- Floating the Mapper, hiding panels you don't use, and moving the hands strip are all remembered.

> 🚧 **Roadmap:** named, switchable workspace presets (a "Combat" layout vs. an "In-Character" layout) and light/dark themes are planned but not in the alpha yet.

## Display settings

**Edit → Display Settings…** controls fonts, colors, the roundtime indicator's position, the hands-strip position, and the external editor used by `#edit`.

## Related

- [Configuration & Rules](Configuration) — highlight colors, aliases, triggers, and macros.
- [The Mapper](Mapper) — the map panel in depth.
- [Scripting](Scripting) — automate from the command bar.
