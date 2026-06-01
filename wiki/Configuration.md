# Configuration & Rules

Genie 5 ships all of Genie 4's **rule engines** — the pattern-driven helpers that color text, expand shortcuts, react to the game, and bind keys. You manage them two ways:

- **The Configuration dialog** — **Edit → Configuration…** opens a tabbed, form-based editor for every rule type. Easiest for browsing and editing.
- **The command bar** — `#`-prefixed commands add and remove rules on the fly, exactly as in Genie 4.

Either way, rules are saved to plain-text `.cfg` files and reloaded automatically next launch. Command syntax follows the **Genie 4 dialect**; when in doubt about a specific option, the Configuration dialog is the reliable surface.

## The rule engines

| Rule | What it does | Commands |
| --- | --- | --- |
| **Aliases** | Expand a typed shortcut into a longer command. | `#alias`, `#unalias` |
| **Triggers / Actions** | Run command(s) automatically when game text matches a pattern. | `#trigger`, `#action` |
| **Highlights** | Color lines (or substrings) that match a pattern. | `#highlight` |
| **Substitutes** | Rewrite matching text before it's displayed. | `#substitute`, `#sub`, `#subs` |
| **Gags** | Hide matching lines from the output entirely. | `#gag`, `#ungag` |
| **Macros** | Bind a keystroke (F-keys, Ctrl/Alt/Shift+key) to command(s). | `#macro` |
| **Variables** | Store reusable values, readable in scripts as `$name`. | `#var`, `#tvar` |
| **Classes** | Named groups that turn sets of rules on/off together. | `#class` |

### Aliases — typing shortcuts

Expand a short token into a full command:

```
#alias {gb} {get my backpack}
```

Now typing `gb` sends `get my backpack`. Remove it with `#unalias {gb}`.

### Triggers / Actions — automatic responses

Run commands when a line of game text matches. Triggers are *responses* to the game, the same model Genie 4 used:

```
#action {stand} when {You stumble to the ground}
```

When "You stumble to the ground" appears, Genie sends `stand`. Patterns can be literal text or regular expressions, and an action can be tagged to a **class** so you can switch groups of them on and off.

> Triggers respond to text; they don't play the game for you. See [Policy Compliance](Policy-Compliance) for where the line is.

### Highlights — coloring text

Make important lines jump out:

```
#highlight {red} {You are bleeding}
#highlight {yellow} {whispers}
```

Highlights support foreground and background colors, whole-line vs. substring matching, and case sensitivity — all editable in the Highlights tab.

### Substitutes — rewriting text

Replace matched text with your own before it's shown (useful for shortening noisy messages). Managed with `#sub` / `#substitute`; list them with `#subs`.

### Gags — hiding lines

Suppress lines you never want to see:

```
#gag {The wind blows gently}
```

Remove with `#ungag`.

### Macros — key bindings

Bind a keystroke to one or more commands:

```
#macro {F2} {prepare 101}
```

Macros support F-keys and `Ctrl` / `Alt` / `Shift` modifiers. The Macros tab captures a keypress for you so you don't have to spell the key name.

### Variables — stored values

Variables hold values you can reuse, including inside scripts (where they read as `$name`):

```
#var weapon longsword
```

- `#var` values **persist** to disk (`variables.cfg`).
- `#tvar` sets a **temporary** variable for the session only.

Genie also exposes ~40 live **game-state** variables (`$health`, `$stance`, `$righthand`, …) automatically — see [Scripting](Scripting#game-state-variables).

### Classes — grouping rules

A **class** is an on/off switch that gates a group of rules. Tag highlights, triggers, substitutes, gags, aliases, or macros with a class name, then flip them all at once:

```
#class {combat} {on}
#class {combat} {off}
```

This is how you keep, say, a full set of combat triggers ready but inactive until you start hunting.

## Where rules are stored

Each rule type saves to its own `.cfg` file (one entry per line, the commands that recreate the rule):

- **Shared baseline** — `Config/aliases.cfg`, `triggers.cfg`, `highlights.cfg`, `substitutes.cfg`, `gags.cfg`, `macros.cfg`, `variables.cfg`, `classes.cfg`.
- **Per-character** — a copy under `Profiles/<Character>-<Account>/`, seeded from the shared baseline the first time that character connects, then independent.

These are Genie 4-format `.cfg` files (not JSON), so they round-trip with the Genie 4 ecosystem. See [Application Folders](Application-Folders) for the full layout. You can hand-edit them with a text editor while Genie is closed.

## Importing from Genie 4

You don't have to recreate any of this by hand — **File → Import from Genie 4…** reads your existing Genie 4 `.cfg` files and folds them in, per-category, with merge/replace options. See [Importing from Genie 4](Importing-Genie4-Config).

## Related

- [Scripting](Scripting) — variables and triggers lead naturally into scripts.
- [The Interface](The-Interface) — where highlights and streams show up.
- [Importing from Genie 4](Importing-Genie4-Config) — bring existing rules across.
