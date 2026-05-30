# Default User Settings — Reference Material

When the project gets to **"Default User Settings configuration"** work (shipping a
sensible out-of-box settings bundle for new Genie 5 users, and/or building a UI
surface to manage user defaults), consult:

## Primary reference

**https://www.elanthia.org/GenieSettings/**

Canonical community-maintained guide to every Genie 4 settings file: what each
config does, what the syntax looks like, and the conventional defaults DR
players use. Covers (verified from the URL fragment `#:~:text=Logging%20Triggers`
that Jason linked):

- Highlights (color rules, classes, match types)
- Names (NameHighlightEngine targets — character / NPC / object names)
- Presets (built-in display roles: speech, whisper, thought, roomName, etc.)
- Aliases (`#alias` shortcuts)
- Triggers / Actions (`#trigger`/`#action` pattern → command response)
- Substitutes (in-line text rewrites)
- Gags (suppress matching lines from the output stream)
- Macros (keystroke → command bindings)
- Variables (`#var`)
- Classes (named groups for bulk enable/disable)
- Window settings (per-window font / color / redirect / timestamps)
- **Logging triggers** (the specific fragment Jason was reading — patterns
  that flip logging on/off automatically; e.g. start logging on entering
  a hunting zone)

## How to use this

- **Default bundle design**: cross-reference the Elanthia recommendations
  against `D:\Genie5Project\src\Genie.App\Settings\DisplaySettings.cs`,
  `WindowSettingsStore` defaults, and the various engine `Add*` calls so a
  fresh install gets the same "feels right" baseline as a long-time Genie 4
  player.
- **UI labelling**: when the Configuration dialog grows new tabs (or its
  existing tabs add "Restore Defaults" buttons), copy the terminology /
  examples from this page so users who already know Genie 4 don't have to
  re-learn vocabulary.
- **Script-compat sanity check**: Tirost's DR-Genie-Scripts assumes Elanthia
  defaults exist (`%kneeling`, named highlight classes like `combat-loot`,
  etc.). Use the page to spot baked-in assumptions before they bite.

## Related

- Configuration-dialog work to date is captured in
  [checkpoint_2026_05_configuration.md](checkpoint_2026_05_configuration.md)
  (Phase 1 / Phase 2 status). When Default Settings work begins, that
  checkpoint is the file map of what already exists to attach to.
- Per-character profile dir layout (where the bundle would land) is in
  [checkpoint_2026_05_23_script_persistence.md](checkpoint_2026_05_23_script_persistence.md).

## Trigger phrase

**"start Default User Settings work"** or **"check the Elanthia settings
reference"** — both should pull up this file and the Elanthia.org URL.
