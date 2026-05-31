# Genie 4 vs Genie 5 — Comprehensive Side-by-Side Comparison

**Prepared:** 2026-05-26
**Genie 5 version:** v5.0.0-alpha.1 (commit `5c422e3`)
**Genie 4 reference:** local clone at `_refs/Genie4/` (143 source files, WinForms + .NET 6, GenieClient/Genie4 upstream)
**Purpose:** Audit feature parity before the alpha ships to select testers. Identify what must change, what should change, and what's safely deferred.

---

## Executive Summary

Genie 5 alpha lands at roughly **85% feature parity** with Genie 4 by surface area, but the missing 15% is intentionally split into three buckets:
- Features deferred to **beta** that don't block initial value (~10%): Plugin host, Auto Log, Themes, Workspace presets, LAMP 2.0 updater, JavaScript scripts.
- Features deferred to **v1.0+** as roadmap vision items (~3%): AI advisor, plugin marketplace, cloud sync.
- Features intentionally **forbidden** by DR policy (~2%): auto-reconnect, agentive AI, auto-walk-while-away.

**Genie 5 also adds ~10% of capability Genie 4 doesn't have**: cross-platform (Win/Mac/Linux), AES-GCM password encryption, per-character profile dirs, Session Recorder, tab-complete script names, Editor-of-choice integration, Genie 4 settings import dialog, modern XAML/MVVM architecture.

### Headline calls before alpha ships

| # | Item | Severity | Action |
|---|---|---|---|
| 1 | **AutoMapper auto-walk** — Pre-publish checklist says "AutoMapper must be working" as a v1 release blocker. Today click-to-goto opens a dialog; no actual walking happens | **Alpha-blocker** per checklist | Decision needed: implement engine-driven CommandQueue auto-walk OR ship the Mapper Helper Script approach. ~1-2 days either way. |
| 2 | **Reconnect config key is a foot-gun** — `Reconnect=true` is the default in `GenieConfig.cs` but not wired to behavior. A future contributor could wire it and ship a policy violation by accident | Low (no behavior today) but worth a code-level guard | Remove the key, or add a `[Obsolete]` attribute + comment explaining it must never be wired |
| 3 | **`MaxReconnectAttempts=10` + 5s delay = 50s wait on transient initial connect failure** | UX, not policy | Reduce to 3 attempts. ~10 min change |
| 4 | **Character display format** — Pre-publish checklist requires `Character-Account` format (`Renucci-MONIL`); not yet rolled out | Pre-publish hygiene | ~30 min — title bar, character dropdown, profile picker, defaults |
| 5 | **Help menu missing entirely** — Genie 4 ships Help with Wiki / Discord / GitHub / Updates / community links; Genie 5 has no Help menu | Beta-eligible but useful for tester orientation | ~30 min to add a stub menu with the most useful 4-5 links |
| 6 | **AI pipeline gating** — AI pipeline exists but is feature-flagged off; needs the in-app privacy notice + whisper/talk/thoughts filtering before any external send | Beta-eligible (today OFF) | Hard requirement when AI mode ships, not when alpha ships |
| 7 | **OS keystore for credentials** — Today machine-bound AES-GCM (correct for local). Genie 4 used XOR'd XML which is worse | Beta enhancement | Move to DPAPI/Keychain/libsecret later |

**Recommendation:** Address items 2, 3, 4 before shipping alpha (about 1 hour total). Decide item 1 explicitly (ship-with vs ship-without-auto-walk). Items 5-7 are honest beta-track work.

---

## Methodology

**Sources consulted:**
1. **Genie 5 source tree** — `src/` (Core + App projects, ~50k LOC)
2. **Genie 5 documentation** — README, CONTRIBUTING, the rest of `docs/`, the published alpha's ALPHA-README, commit history
3. **Internal development notes** — design backlog, policy compliance review, pre-publish checklist, terminology, milestone checkpoints (not in this repo)
4. **Empirical findings** — recorded session captures from live DR play, parser diff reports, verb-inventory experiments (not in this repo)
5. **Genie 4 source tree** — full local clone of the [GenieClient](https://github.com/GenieClient) organization repos (~143 .cs files in the main client)
6. **Codebase walk** — two parallel cataloguing passes, ~20 minutes each, cross-referenced

**Limitations:**
- The Genie 4 inventory was assembled by an agent reading source files; some plugin-API specifics may be incomplete (interface enumeration would require deeper exploration).
- Genie 4 settings keys were enumerated from `Lists/Config.cs`; a few edge-case config keys may not be covered.
- Both inventories cite source-file locations so claims are verifiable.

**Status legend used throughout:**
- ✅ **Parity** — Genie 5 has it
- 🆕 **Better** — Genie 5 has it AND improves on Genie 4
- ⚠️ **Partial** — Genie 5 has some of it
- 🗓 **Beta** — Deferred to v5.0 beta; planned with design notes in `backlog.md`
- 🎯 **v1.0+** — Roadmap item; design exists but post-alpha
- ❌ **Missing** — Not present and no clear plan
- 🛑 **Forbidden** — Genie 4 has it, Genie 5 won't ship it for DR policy compliance reasons

**Alpha decision legend:**
- 🚀 **SHIP** — Good as-is
- 🔧 **FIX BEFORE ALPHA** — Action required
- 🗓 **BETA OK** — Document the gap, defer
- 🛑 **NEVER** — Policy violation

---

## 1. Menus

### File menu

| Menu Item | Genie 4 | Genie 5 | Status | Notes |
|---|---|---|---|---|
| Connect... | ✅ | ✅ | 🚀 SHIP | Genie 5 unifies "Connect" + "Connect Using Profile" into one dialog with profile picker |
| Connect Using Profile... | ✅ separate | ✅ merged | 🚀 SHIP | Merged into Connect dialog |
| Disconnect | ✅ | ✅ | 🚀 SHIP | |
| Open Directory... → submenu (Genie/Scripts/Maps/Plugins/Logs/Art) | ✅ submenu | ⚠️ partial | 🗓 BETA OK | Genie 5 has Open Maps Folder + Open Recordings Folder; no submenu yet |
| Auto Log (toggle) | ✅ | 🗓 backlog | 🗓 BETA OK | Backlog entry "Auto Log — text-mode session log"; ~80 LOC. Not blocking alpha. |
| Open Log In Editor | ✅ | 🗓 backlog | 🗓 BETA OK | Depends on Auto Log |
| Auto Reconnect (toggle) | ✅ default ON | 🛑 not shipped | 🛑 NEVER | Forbidden by DR policy. Config key exists but not wired (see #2 in headline calls). |
| Classic Connect Window (toggle) | ✅ | n/a | 🚀 SHIP | We don't have a legacy dialog to fall back to |
| Ignores/Gags Enabled (master toggle) | ✅ | ❌ | 🗓 BETA OK | Per-rule IsEnabled exists; no master switch |
| Triggers Enabled (master toggle) | ✅ | ❌ | 🗓 BETA OK | Per-rule IsEnabled exists; no master switch |
| Plugins Enabled (master toggle) | ✅ | n/a | 🗓 BETA OK | No plugin host yet |
| AutoMapper Enabled (master toggle) | ✅ | ❌ | 🗓 BETA OK | Mapper is always active; no master off-switch |
| Images Enabled (toggle) | ✅ | ❌ | 🗓 BETA OK | `ShowImages` setting exists; no `<image>` rendering yet |
| Mute Sounds (toggle) | ✅ | n/a | 🗓 BETA OK | No audio yet |
| Show Raw Data (toggle) | ✅ | 🗓 backlog | 🗓 BETA OK | Backlog "Raw XML Window" entry; half-day for v1 |
| Update Maps from Official Repo... | n/a | 🆕 | 🚀 SHIP | Genie 5 addition — pulls from github.com/GenieClient/Maps |
| Open Maps Folder | ✅ via Open Directory | ✅ direct | 🆕 | Direct menu in Genie 5 |
| Change Maps Directory... | n/a | 🆕 | 🚀 SHIP | Genie 5 addition for git-clone workflow |
| Import from Genie 4... | n/a | 🆕 | 🚀 SHIP | Just-shipped (commit `5889182`) — migrates 8 settings types with Global/per-character routing |
| Record Session (raw XML, toggle) | n/a | 🆕 | 🚀 SHIP | Genie 5 addition — captures raw XML to `Logs/raw_session_*.xml` |
| Open Recordings Folder | n/a | 🆕 | 🚀 SHIP | Pair with Record Session |
| Performance Test Parse (dev) | ✅ | partial via Console | 🚀 SHIP | Genie 5's TestHarness REPLAY mode covers this |
| Exit | ✅ | ✅ | 🚀 SHIP | |

### Edit menu

| Menu Item | Genie 4 | Genie 5 | Status | Notes |
|---|---|---|---|---|
| Paste Multi Line | ✅ | ❌ | 🗓 BETA OK | Clipboard-with-newlines as separate commands. Useful but not critical. |
| Configuration... | ✅ tabbed dialog | ✅ tabbed dialog | ⚠️ partial | Genie 5 has tabs but the UX needs a holistic pass per `backlog.md` "Configuration dialog UX pass" |
| Update Images | ✅ | n/a | 🗓 BETA OK | No image rendering |
| Display Settings... | n/a | 🆕 | 🚀 SHIP | Genie 5 addition — font, colors, RoundTime position, hands strip position, editor path |
| Profile → Load Profile... | ✅ | ✅ merged | 🚀 SHIP | Merged into Connect dialog |
| Profile → Save Profile | ✅ | ✅ merged | 🚀 SHIP | |
| Profile → Include Password In Profile (toggle) | ✅ | always-on via AES-GCM | 🆕 | Genie 5 always encrypts (no toggle — password storage is either AES-GCM encrypted on disk OR absent entirely) |

### Window menu

| Menu Item | Genie 4 | Genie 5 | Status | Notes |
|---|---|---|---|---|
| Show/hide each dockable | ✅ | ✅ | 🚀 SHIP | Genie 5 supports same set: Game / Vitals / Room / Backpack / Mapper / Logons / Talk / Whispers / Thoughts / Combat |
| Hands Strip toggle | n/a | ✅ | 🚀 SHIP | Genie 5 addition |
| Hands Strip Position (Top/Bottom) | n/a | ✅ | 🚀 SHIP | |
| Roundtime Position (Command Bar / Hands Strip) | n/a | ✅ | 🚀 SHIP | |
| Status Bar toggle | ✅ | ✅ | 🚀 SHIP | |
| Game Window → Game Text / Echo / Script Lines per-tag toggle | n/a | ✅ | 🆕 | Genie 5 addition, just shipped (commit `4a8986b`) |
| Float Mapper Window | n/a | ✅ | 🚀 SHIP | Dock.Avalonia FloatDockable; can re-dock by dragging |
| Reset Layout | n/a | ✅ | 🚀 SHIP | |

### Layout menu

| Menu Item | Genie 4 | Genie 5 | Status | Notes |
|---|---|---|---|---|
| Load Layout... / Save Layout As... | ✅ | ❌ | 🗓 BETA OK | Backlog "Workspace presets (Combat/Healing/In-Character layouts)" |
| Load Default Layout | ✅ | ⚠️ via Reset Layout | 🚀 SHIP | Equivalent functionality |
| Save Default Layout / Save Sized Default | ✅ | ❌ | 🗓 BETA OK | Multi-preset story |
| Basic Layout | ✅ | ❌ | 🗓 BETA OK | |
| Icon Bar → Dock Top/Bottom | ✅ submenu | ⚠️ via Hands Strip Position | 🚀 SHIP | Genie 5's hands strip serves the same role |
| Script Bar → Dock Top/Bottom | ✅ | 🆕 always above command bar | 🚀 SHIP | Genie 5 Script Bar is fixed-position but auto-hides when empty |
| Health Bar → Dock Top/Bottom | ✅ | ⚠️ via Status Bar toggle | 🚀 SHIP | Genie 5's status bar is fixed at bottom |
| Magic Panels (toggle) | ✅ | ❌ | 🗓 BETA OK | Spell-list panel; data exists via `percWindow` stream |
| Align Input to Game Window | ✅ | ❌ | 🗓 BETA OK | Niche |
| Always On Top | ✅ | ❌ | 🗓 BETA OK | Trivial Avalonia feature (`Topmost=true`); ~5 LOC |

### Script menu

| Menu Item | Genie 4 | Genie 5 | Status | Notes |
|---|---|---|---|---|
| **Entire menu** | ✅ Script menu | ❌ | 🗓 BETA OK | Genie 5 has command-bar `.script`, `#stop`, `#scripts`, plus Script Bar UI, plus `#edit`. Genie 4 Script menu provides muscle-memory entry points; functionality is present but discovery isn't |
| Script Explorer... | ✅ tree browser | ❌ | 🗓 BETA OK | Scripts dir + Script Bar are the entry points today |
| Update Scripts | ✅ LAMP | ❌ | 🗓 BETA OK | LAMP 2.0 backlog |
| Show Active Scripts | ✅ | ✅ via Script Bar | 🆕 | Genie 5 always-visible; Genie 4 was on-demand |
| Trace Active Scripts (debug toggle) | ✅ | partial | 🗓 BETA OK | Genie 5 has `dbg:10` script-level debug; no menu toggle |
| Pause All / Resume All Scripts | ✅ | ❌ | 🗓 BETA OK | Genie 5 has Stop (#stop) and StopAll (#stopall); no pause/resume primitive |
| Abort All Scripts | ✅ | ✅ via #stopall | 🚀 SHIP | |

### AutoMapper menu

| Menu Item | Genie 4 | Genie 5 | Status | Notes |
|---|---|---|---|---|
| Show Window | ✅ | ✅ via Window menu | 🚀 SHIP | |
| Update Maps | ✅ LAMP | ✅ direct via File menu | 🆕 | |
| Script Settings | ✅ FormMapperSettings | ❌ | 🗓 BETA OK | Genie 4's AutoMapper Script Settings dialog drives a community `.cmd` walker script. Genie 5 design decision pending (engine-driven vs script-driven). |

### Plugins menu

| Menu Item | Genie 4 | Genie 5 | Status | Notes |
|---|---|---|---|---|
| **Entire menu** | ✅ | ❌ | 🎯 v1.0+ | Plugin host not built. Roadmap "Modern Plugin Marketplace" |

### Help menu

| Menu Item | Genie 4 | Genie 5 | Status | Notes |
|---|---|---|---|---|
| Check For Updates / AutoUpdate / Force Update | ✅ | ❌ | 🗓 BETA OK | LAMP 2.0 |
| Load Test Client | ✅ | n/a | 🗓 BETA OK | Could use GameCode picker on Connect dialog (already supports Test) |
| Latest Release Page | ✅ | ❌ | **🔧 FIX BEFORE ALPHA** | Helpful for testers reporting; 10 min to add |
| Discord / GitHub / Wiki / Play.net / Elanthipedia / Lich Discord links | ✅ | ❌ | **🔧 FIX BEFORE ALPHA** | Tester onboarding; ~30 min for a full Help menu |

### Profile menu (Genie 4 has its own menu)

| Menu Item | Genie 4 | Genie 5 | Status | Notes |
|---|---|---|---|---|
| Load Profile / Save Profile / Include Password | ✅ separate menu | ✅ folded into Connect dialog | 🚀 SHIP | Genie 5 doesn't have a top-level Profile menu; functionality is in the Connect dialog. Worth a menu for muscle memory? Marginal. |

**Menu rollup:**
- Critical missing: **Help menu** (links). ~30 min — should-add before alpha for tester orientation.
- Defer to beta: Layout save/load, Plugins menu (depends on host), Script menu (functionality exists, discovery doesn't), Auto Log / Open Log In Editor, master toggles for engines.
- Never: Auto Reconnect.

---

## 2. Settings (cfg keys / configuration)

Genie 4 has 60+ config keys in `Lists/Config.cs`. Genie 5 has rough equivalents for most, with a 14-item backlog `Genie 4 Config-Option parity audit` capturing the gap.

### Already-shipped parity

| Key | Genie 4 default | Genie 5 default | Notes |
|---|---|---|---|
| `commandchar` | `#` | `#` | ✅ Match |
| `scriptchar` | `.` | `.` | ✅ Match |
| `separatorchar` | `;` | `;` | ✅ Match (defaults match; need to verify Genie 5 actually splits — backlog "verify multi-command separator") |
| `scriptextension` | `cmd` | `cmd` | ✅ Match |
| `prompt` | `> ` | `> ` | ✅ Match |
| `mycommandchar` | `/` | `/` | ⚠️ Match in setting but Genie 5 doesn't use it for anything |
| `spelltimer` | True | True | ✅ Match (Genie 5 cast bar shipped) |
| `autoupdate` | False | False | ✅ Match (both default OFF; Genie 4 has LAMP, Genie 5 deferred) |

### Shipped in Genie 5 with different default

| Key | Genie 4 default | Genie 5 default | Why |
|---|---|---|---|
| `showlinks` | False | True | Genie 5 always-on; better UX |
| `keepinputtext` | False | False | Match |
| `weblinksafety` | True | True | Match (confirm-before-open URLs) |

### Deferred to beta — backlog item "Genie 4 Config-Option parity audit"

These are small individual items, mostly UI-toggle wiring. Total estimated: 2-3 days. Each is ~15-30 lines.

| Key | Default | What it does | Status |
|---|---|---|---|
| `abortdupescript` | True | If a script with the same name is already running, abort the duplicate | 🗓 BETA OK |
| `editor` | `notepad.exe` | External editor path for "Open Log In Editor" + future "Edit Script" | ✅ shipped as `Display.EditorPath` |
| `maxgosubdepth` | 50 | Script-engine GOSUB recursion limit | ✅ shipped (`GenieConfig.MaxGoSubDepth = 50`) |
| `maxrowbuffer` | 5 | Output buffering line count | ⚠️ different semantics in Genie 5 |
| `promptbreak` | True | Insert blank line before each `<prompt>` | 🗓 BETA OK |
| `promptforce` | True | Force prompt display when server omits | 🗓 BETA OK |
| `condensed` | False | Compact display mode | 🗓 BETA OK |
| `triggeroninput` | True | Run triggers against user input lines, not just server | ✅ shipped |
| `roundtimeoffset` | 0 | Latency-comp adjustment to RT display | 🗓 BETA OK |
| `weblinksafety` | True | Confirm-before-open on URL clicks | ✅ shipped |
| `monstercountignorelist` | regex | Patterns to exclude from monster count | 🗓 BETA OK |
| `scripttimeout` | 5000 ms | Max runtime per script | ✅ shipped |
| `ignorescriptwarnings` | False | Suppress script-engine warnings | 🗓 BETA OK |
| `parsegameonly` | False | Skip parser on user input | 🗓 BETA OK |
| `ignoreclosealert` | False | Suppress confirm-on-close | 🗓 BETA OK |
| `sizeinputtogame` | False | Align input bar to game width | 🗓 BETA OK |
| `connectscript` | empty | Auto-run a named script on connect | 🗓 BETA OK (per-profile, in backlog) |
| `connectstring` | `FE:GENIE...` | Client-ID announcement string | ✅ shipped (engine-controlled) |
| `servertimeout` + `servertimeoutcommand` | 180s / fatigue | Keep-alive verb on idle | 🗓 BETA OK |
| `usertimeout` + `usertimeoutcommand` | 300s / quit | User-side idle disconnect verb | 🗓 BETA OK |
| `requiresignedplugins` | False | Plugin signature verification | 🎯 v1.0+ (no plugin host) |
| Per-data-dir overrides (`artdir`, `logdir`, `configdir`, `plugindir`, `mapdir`, `scriptdir`, `sounddir`) | local relative dirs | resolved via `LocalDirectoryService` | 🗓 BETA OK (overrides not exposed in UI) |
| Repository URLs (`scriptrepo`, `maprepo`, `pluginrepo`, `artrepo`) | empty | empty | 🎯 v1.0+ (LAMP 2.0) |
| Lich integration (`rubypath`, `cmdpath`, `lichpath`, `licharguments`, `lichserver`, `lichport`, `lichstartpause`) | typical | n/a | 🗓 BETA OK (Genie 5 has LichProxy mode; no auto-launch yet) |

### Forbidden by DR policy

| Key | Genie 4 default | Genie 5 status | Action |
|---|---|---|---|
| `reconnect` | True | Key exists, NOT wired to behavior | **Foot-gun**: remove the key or annotate `[Obsolete]` — see headline call #2 |

### Genie 5-only additions

- `frontendid` (`GENIE`/`STORM`) — FE handshake selector (CLI/code-controllable; UI removed after FE:STORM hypothesis disconfirmed)
- `RoundTimeOnHandsStrip` — RT badge position
- `ShowGameText` / `ShowEchoText` / `ShowScriptText` — per-tag visibility
- `EditorPath` — external editor for `#edit`
- `MapBackgroundHex` — Mapper canvas background

---

## 3. Rule Engines

This is the largest area of genuine parity. **Genie 5 ships all of Genie 4's rule engines.**

| Engine | Genie 4 | Genie 5 | Class scope | Persistence | Status |
|---|---|---|---|---|---|
| **Aliases** | `Lists/Aliases.cs` + `#alias` | `Aliases/AliasEngine.cs` + `#alias`/`#unalias` | ✅ wired | `aliases.cfg` (JSON) | 🚀 SHIP |
| **Triggers** | `Lists/Globals.cs` Triggers | `Triggers/TriggerEngineFinal.cs` + `#trigger`/`#action` | ✅ wired | `triggers.cfg` (JSON) | 🚀 SHIP |
| **Highlights** | `Lists/Highlights.cs` (3 subclasses) | `Highlights/HighlightEngine.cs` + `#highlight` | ✅ wired | `highlights.cfg` (JSON) | 🚀 SHIP |
| **Substitutes** | `Lists/Globals.cs` Subs + `#sub` | `Substitutes/SubstituteEngine.cs` + `#substitute`/`#sub`/`#subs` | ✅ wired | `substitutes.cfg` (JSON) | 🚀 SHIP |
| **Gags** | `Lists/Globals.cs` Gags + `#gag` | `Gags/GagEngine.cs` + `#gag`/`#ungag` | ✅ wired | `gags.cfg` (JSON) | 🚀 SHIP |
| **Macros** | `Lists/Macros.cs` + `#macro` | `Macros/MacroEngine.cs` + `#macro` | ✅ wired (just-shipped, commit `5889182`) | `macros.cfg` (JSON) | 🚀 SHIP |
| **Variables** | `Lists/Globals.cs` Variables + `#setvar` | `Variables/VariableEngine.cs` + `#var`/`#tvar` | n/a (data, not rules) | `variables.cfg`/`tvars.cfg` | 🚀 SHIP |
| **Classes** | `Lists/Classes.cs` + `#class` | `Classes/ClassEngine.cs` + `#class` | n/a (gating mechanism) | `classes.cfg` (JSON) | 🚀 SHIP |
| **Names** | `Lists/Names.cs` + `#name` | `Highlights/NameHighlightEngine.cs` | ❌ no class scope | `names.cfg` (JSON) | ⚠️ partial — class scope missing |
| **Presets** | `Lists/Globals.cs` Presets + UI | `Presets/PresetEngine.cs` (parse-side wiring; render-side colors not yet exposed) | n/a | `presets.cfg` (JSON) | 🗓 BETA OK (backlog "Revisit Preset Color Highlights") |

### Rule-engine sub-features

| Sub-feature | Genie 4 | Genie 5 | Status |
|---|---|---|---|
| Highlight sound playback (`SoundFile` on rule) | ✅ | ❌ (no audio system) | 🗓 BETA OK |
| Highlight "match whole line" vs "substring" mode | ✅ | ✅ | 🚀 SHIP |
| Highlight foreground + background colors | ✅ | ✅ | 🚀 SHIP |
| Highlight case-sensitive flag | ✅ | ✅ | 🚀 SHIP |
| Trigger regex with `/pattern/i` syntax | ✅ | ✅ | 🚀 SHIP |
| Trigger `eval` expression triggers | ✅ via `e/` | ⚠️ via `def(...)` | 🚀 SHIP (different syntax) |
| Trigger fire-on-input (vs server only) | ✅ via `triggeroninput` | ✅ | 🚀 SHIP |
| Macro keybind: F-keys, Ctrl+X, Alt+X, Shift+X | ✅ | ✅ | 🚀 SHIP |
| Variable types: SaveToFile / Temporary / Reserved | ✅ | ⚠️ via `Scope` enum (Global/Script/Tvar) | 🚀 SHIP (semantically equivalent) |
| Reserved variables ($health, $mana, $stance, etc.) | ✅ ~30 vars | ✅ ~40 vars via `ScriptGlobalsSync` | 🆕 |
| Per-rule ClassName for filtering | ✅ | ✅ for Highlights/Triggers/Substitutes/Gags/Aliases/Macros; ❌ for Names | ⚠️ partial |
| Command-bar syntax for `class:foo` modifier | ✅ on most | ❌ for Aliases/Macros (engine support, but `#alias add … class:foo` parser ext not done) | 🗓 BETA OK |
| .cfg round-trip with class name | ✅ | ⚠️ for Aliases/Macros — engine supports but serializer not updated | 🗓 BETA OK |

**Rule-engine rollup:**
- All core engines ship in Genie 5 with full Genie 4 parity.
- Two small remaining gaps: command-bar syntax for `class:foo` on aliases/macros (~30 LOC each), and Presets render-side color application (backlog item).
- Names engine missing class scope; lower priority (it's the internal player-name highlighter, not a user-rule engine).

---

## 4. Script Engine

### Native `.cmd` script support

Both Genie 4 and Genie 5 support the Wizard-derived `.cmd` script language. **Genie 5 is a faithful port** — same vocabulary, same `$variable` substitution semantics, same `MATCH`/`WAITFOR`/`GOSUB`/`GOTO` flow control.

| Feature | Genie 4 | Genie 5 | Status |
|---|---|---|---|
| Labels (`:label`, `label:`) | ✅ | ✅ | 🚀 SHIP |
| `GOTO` / `GOSUB` / `RETURN` | ✅ | ✅ | 🚀 SHIP |
| `MATCH` / `MATCHRE` / `WAITFOR` / `WAITFORRE` | ✅ | ✅ | 🚀 SHIP |
| `PUT` / `SEND` (send to game) | ✅ | ✅ | 🚀 SHIP |
| `#put` alias | n/a | ✅ | 🆕 |
| `ECHO` (display text, with optional `>window` `#color`) | ✅ | ✅ | 🚀 SHIP |
| `PAUSE` / `WAIT` (sleep N seconds) | ✅ | ✅ | 🚀 SHIP |
| `waitpause` (sleep until current RT expires) | ✅ | ✅ (Task #113) | 🚀 SHIP |
| `if_*` conditional slots, `IF ... THEN ... ELSE ... ENDIF` | ✅ | ✅ | 🚀 SHIP |
| `def(name)` expression | ✅ | ✅ (Task #114) | 🚀 SHIP |
| Variables (`var foo = bar`, `$foo` substitution) | ✅ | ✅ | 🚀 SHIP |
| `%1 %2 ... %0` argument substitution | ✅ | ✅ | 🚀 SHIP |
| `#var` / `#tvar` (session globals) | ✅ | ✅ | 🚀 SHIP |
| `EVAL` / `EVALMATH` (math expression) | ✅ | ⚠️ via `def(...)` | 🚀 SHIP |
| `random` (random number) | ✅ | ⚠️ verify | TBD |
| `counter` | ✅ | ⚠️ verify | TBD |
| `INCLUDE <script>` (parse-time inclusion) | ✅ | ✅ | 🚀 SHIP |
| `EXIT` (stop script) | ✅ | ✅ | 🚀 SHIP |
| `#stop` / `#stopall` (kill script from command bar) | ✅ | ✅ (Task #117) | 🚀 SHIP |
| `#scripts` (list running) | ✅ | ✅ (Task #117) | 🚀 SHIP |
| `#edit` (open in external editor) | ✅ | ✅ (Task #188) | 🚀 SHIP |
| Tab-complete script names in command bar | ❌ | 🆕 (Task #187) | 🆕 |
| Type-ahead budget management | ✅ | ✅ (`TypeAheadSession`) | 🚀 SHIP |
| RT-aware command queueing | ✅ | ✅ (`CommandQueue`) | 🚀 SHIP |
| GOSUB recursion limit | ✅ `maxgosubdepth=50` | ✅ `MaxGoSubDepth=50` | 🚀 SHIP |
| Script timeout | ✅ `scripttimeout=5000` | ✅ `ScriptTimeout=5000` | 🚀 SHIP |
| Abort-on-undefined-var | n/a | 🆕 (Task #120) — Genie 4 silently expanded to empty | 🆕 |

### JavaScript `.js` script support

| Feature | Genie 4 | Genie 5 | Status |
|---|---|---|---|
| `.js` array scripts via Jint engine | ✅ | ❌ | 🗓 BETA OK |
| `js`/`javascript`/`jsblock`/`jscall` script commands | ✅ | ❌ | 🗓 BETA OK |

**Backlog item**: "JavaScript scripting support (`.js` array scripts)" — 2-3 days for v1 using Jint (pure-managed, no native binary distribution headache). Compliance: must default to NO host access (Jint `AllowClr(false)`); opt-in per-script via header comment.

### Lich .rb script support

| Feature | Genie 4 | Genie 5 | Status |
|---|---|---|---|
| Launch Lich proxy + run `.rb` scripts | ✅ (`Config.LichPath`/`LichArguments`) | ⚠️ via LichProxy mode (manual Lich launch by user) | 🚀 SHIP |
| Auto-launch Lich on connect | ✅ | ❌ | 🗓 BETA OK |

**Script-engine rollup:**
- `.cmd` parity: ~99% verified against 4-5 community scripts (Task #112).
- `.js` support is the only major gap; design exists in backlog.

---

## 5. UI Panels / Windows

Both clients support a flexible dockable-panel layout, but the implementation tech is completely different (WinForms MDI vs. Avalonia + Dock.Avalonia).

### Dockable windows

| Window | Genie 4 | Genie 5 | Default visible | Notes |
|---|---|---|---|---|
| **Game** (main text) | ✅ | ✅ | both | Genie 5 has per-tag visibility filter (Game/Echo/Script) |
| **Vitals** (health/mana/spirit/stamina/concentration bars) | ✅ ComponentBars | ✅ VitalsTool | G4: yes, G5: hidden (Status Bar duplicates it) | |
| **Inventory** / **Backpack** | ✅ | ✅ | both | |
| **Mapper** | ✅ MapForm | ✅ MapperTool | both | |
| **Logons** | ✅ | ✅ | both | |
| **Talk** | ✅ | ✅ | both | |
| **Whispers** | ✅ | ✅ | both | |
| **Thoughts** | ✅ | ✅ | both | |
| **Combat** | ✅ | ✅ | both | Genie 5 has this active-by-default in the bottom-left tab cluster |
| **Familiar** | ✅ | ❌ | 🗓 BETA OK | Backlog item "Familiar / Death / Assess stream windows" |
| **Death** | ✅ | ❌ | 🗓 BETA OK | Same backlog item |
| **Log** (system messages) | ✅ | ❌ | 🗓 BETA OK | Routed to Game window's System color in Genie 5 |
| **Debug** (parser trace) | ✅ | partial | 🗓 BETA OK | Genie 5 has `[dbg:N]` script-level traces |
| **Conversation** (NPC speech) | ✅ | ❌ | 🗓 BETA OK | Niche; few users |
| **Raw** (raw XML inspector) | ✅ | 🗓 backlog | 🗓 BETA OK | "Raw XML Window" backlog entry, half-day for v1 |
| **Active Spells** (`percWindow` stream) | ✅ | ❌ | 🗓 BETA OK | Data flows through parser; just no UI tab |
| **Portrait** | ✅ | ❌ | 🗓 BETA OK | Niche |
| **Room** (room title/description/exits) | n/a as separate | ✅ | 🆕 | Genie 5 splits room from game text into its own panel |
| **Hands Strip** | ✅ within icon bar | ✅ separate strip | 🆕 | Genie 5 dedicated; toggleable position |
| **Script Bar** | ✅ | ✅ | both | Genie 5 auto-hides when empty (cleaner than Genie 4's always-visible) |

### Default layout

| Aspect | Genie 4 | Genie 5 |
|---|---|---|
| Layout shape | User-configured MDI; default = Game center | 3-column: Room+Streams left / Game+Mapper center / Backpack right |
| Status bar | Optional | Yes by default (Wrayth-style vitals bars at bottom) |
| Hands strip | In icon bar | Dedicated strip, default below status bar |

**UI rollup:**
- 10 panels parity; 7 panels deferred (mostly niche stream windows + Raw + Portrait + Active Spells).
- Genie 5 has a more opinionated default layout (3-column) — matches the "ship-ready" arrangement the user wanted.

---

## 6. Plugin System

| Feature | Genie 4 | Genie 5 | Status |
|---|---|---|---|
| Plugin host (.NET DLL plugin API) | ✅ `Core/PluginHost.cs` + `Core/LegacyPluginHost.cs` | ⚠️ scaffolding only (`Extensions/` interfaces) | 🎯 v1.0+ |
| Plugin DLL loading | ✅ | ❌ | 🎯 v1.0+ |
| Plugin signature verification | ✅ `requiresignedplugins` | ❌ | 🎯 v1.0+ |
| Plugin manager UI | ✅ `Forms/FormPlugins.cs` | ❌ | 🎯 v1.0+ |
| Plugin marketplace | ❌ | 🎯 backlog "Modern Plugin Marketplace" | 🎯 v1.0+ |
| Built-in extensions | ❌ | ⚠️ `ExpTrackerExtension`, `InfoTrackerExtension` | 🆕 (different model) |

**Plugin rollup:**
- This is the largest single feature area absent from Genie 5.
- The roadmap target is v1.0+ — a "Modern Plugin Marketplace" with one-click install, ratings, signed packages, sandboxing.
- Built-in extensions (Genie 5 addition) are a stepping stone: same API surface that a plugin would use, but in-process.
- **Alpha-acceptable**: Genie 5 ships without plugin host. ALPHA-README already calls this out: "No plugin host (Genie 4 plugin DLLs won't load)."

---

## 7. AutoMapper

| Feature | Genie 4 | Genie 5 | Status |
|---|---|---|---|
| Map data format (zone XML) | ✅ canonical | ✅ identical | 🚀 SHIP (round-trip via `Genie4MapImporter` / `Genie4MapExporter`) |
| Map rendering (zone canvas) | ✅ | ✅ `MapCanvas` | 🚀 SHIP |
| Click-to-go on map | ✅ left-click | ✅ right-click context menu | 🆕 |
| **Auto-walk between rooms** | ✅ via `.automapper` script | ❌ click opens dialog only | **🔧 FIX BEFORE ALPHA** (per pre-publish checklist this is a v1 release blocker) |
| Map fingerprinting (title + exits) | ✅ | ✅ `MapFingerprint.cs` | 🚀 SHIP |
| Auto-detect zone from current room | ⚠️ via script | ✅ engine | 🆕 |
| Less Obvious Paths display | ✅ | ✅ as clickable buttons | 🚀 SHIP |
| Editable room Notes | ✅ | ✅ inline editor, saves to zone XML | 🚀 SHIP |
| Stale-zone warning | ❌ | 🆕 "may be stale" badge after 30 days | 🆕 |
| Auto-center on current room | ✅ via script | ✅ engine (just-shipped) | 🚀 SHIP |
| Zone update from official repo | ✅ via LAMP | ✅ via File menu (`MapsUpdater` + `GithubContentsSource`) | 🆕 |
| Multi-zone navigation (cross-zone pathing) | ✅ via script | ❌ | 🗓 BETA OK |
| User-walk vs auto-walk vs drag modes | ✅ | ❌ (no auto-walk yet) | 🗓 BETA OK |
| Per-class mapper script | ✅ AutoMapper Script Settings dialog | ❌ | 🗓 BETA OK |
| Sigil walk / search walk / caravan / broom_carpet / iceroadcollect | ✅ via script | ❌ | 🗓 BETA OK |
| Map visual: zoom, pan | ✅ basic | ✅ with mouse-wheel zoom | 🚀 SHIP |
| Map visual: room color by exit type | ❌ | 🆕 cyan for vertical, green for special, grey for compass | 🆕 |
| Map visual: room labels from Notes | ❌ | 🆕 | 🆕 |
| Float mapper to separate window | ❌ | 🆕 (Dock.Avalonia FloatDockable) | 🆕 |

**AutoMapper rollup:**
- Data + rendering: Genie 5 is ahead.
- Auto-walking: Genie 5 punted. This is the ONE alpha-blocker the pre-publish checklist explicitly named.
- **Decision needed**: implement engine-driven auto-walk (via `Commands.ProcessInput` + `CommandQueue` for RT gating) OR ship the "Mapper Helper Script" approach where a community `.cmd` script handles routing. Both are in the backlog ("Revisit Mapper", "Concept: Mapper Helper Script").
- Estimated cost: half-day for the helper-script approach (minimal), 1-2 days for engine-integrated.

---

## 8. Logging

| Feature | Genie 4 | Genie 5 | Status |
|---|---|---|---|
| **Auto Log** (rendered text to disk) | ✅ `autolog` config + File menu | 🗓 backlog | 🗓 BETA OK — ~80 LOC, see backlog |
| **Open Log In Editor** menu | ✅ | 🗓 backlog | 🗓 BETA OK (depends on Auto Log) |
| **Session XML capture** | partial (built-in XML stream save) | 🆕 explicit File → Record Session toggle | 🆕 |
| **REC indicator in title bar** | ❌ | 🆕 (red 🔴 REC) | 🆕 |
| **Error log** | ✅ `errors.log` | ✅ `ErrorLog.cs` | 🚀 SHIP |
| **Debug log** | ✅ via `-d` CLI flag | partial (script-level `[dbg:N]`) | 🗓 BETA OK |
| **Per-character log files** | ✅ | n/a yet (Auto Log not shipped) | 🗓 BETA OK |
| **Log directory configurable** | ✅ `logdir` config | ✅ resolved via `LocalDirectoryService` | 🚀 SHIP (override UI deferred) |

**Logging rollup:**
- Genie 5 has session XML capture (Genie 4 partial; Genie 5 explicit + UI).
- Genie 5 missing Auto Log (rendered text). ~80 LOC; not blocking alpha.

---

## 9. Updater (LAMP)

| Feature | Genie 4 | Genie 5 | Status |
|---|---|---|---|
| LAMP auto-updater | ✅ separate `Lamp.exe` | ❌ | 🎯 v1.0+ |
| Check For Updates menu | ✅ | ❌ | 🎯 v1.0+ |
| Auto-update on startup | ✅ | ❌ | 🎯 v1.0+ |
| Update plugins / maps / scripts independently | ✅ | ⚠️ only maps (File menu) | 🆕 (partial — maps repo updater shipped) |
| Update channels (stable/beta/nightly) | ❌ | 🎯 backlog | 🎯 v1.0+ |
| Auto-update LAMP itself | ✅ `autoupdatelamp` | ❌ | 🎯 v1.0+ |

**Updater rollup:**
- This is the second-largest feature area absent. LAMP 2.0 is the canonical roadmap item.
- Alpha-acceptable workaround: testers download a new zip when we ship updates. Painful but fine for select-few alpha.
- ALPHA-README explicitly calls this out: "No auto-update (LAMP 2.0 is on the roadmap)."

---

## 10. Images & Audio

### Images (`<image>` tags from DR)

| Feature | Genie 4 | Genie 5 | Status |
|---|---|---|---|
| Render `<image>` tags inline in game text | ✅ | ❌ | 🗓 BETA OK |
| Update Images command | ✅ | ❌ | 🗓 BETA OK |
| Show Images toggle | ✅ | ✅ config bool exists; no rendering | 🗓 BETA OK |
| Art directory | ✅ `Art/` | placeholder | 🗓 BETA OK |

### Audio

| Feature | Genie 4 | Genie 5 | Status |
|---|---|---|---|
| WAV playback on highlight match | ✅ | ❌ | 🗓 BETA OK |
| Mute toggle | ✅ `muted` config | placeholder | 🗓 BETA OK |
| Sound directory | ✅ | placeholder | 🗓 BETA OK |
| System speech (TTS) | ✅ via SpeechSynthesizer | ❌ | 🗓 BETA OK |

**Image/Audio rollup:**
- Both deferred. Image rendering is a backlog item; audio is unstated but related to compliance (sound triggers are common in combat-tracking scripts).
- ALPHA-README acknowledges: "No audio playback yet" (implicit — `PlaySounds` setting exists but no playback code).

---

## 11. Profile Management

| Feature | Genie 4 | Genie 5 | Status |
|---|---|---|---|
| Saved connection profiles | ✅ XML files | ✅ JSON via `ProfileStore.cs` | 🚀 SHIP |
| Tree-view of accounts/games in connect dialog | ✅ | ✅ flat list (simpler) | 🚀 SHIP |
| Password encryption on disk | ✅ XOR'd in XML | 🆕 AES-256-GCM with machine-bound key (`ProfileCrypto.cs`) | 🆕 |
| Include Password in Profile (toggle) | ✅ optional | always-encrypted-if-saved | 🆕 |
| Per-character config directory | ✅ | ✅ `Profiles/{Char}-{Acct}/` | 🚀 SHIP |
| Per-profile rule overrides | ✅ | ✅ via per-character config dir | 🚀 SHIP |
| Per-profile layout state | ✅ | partial (Dock.Avalonia state save/load not yet exposed) | 🗓 BETA OK |
| Profile notes | ✅ via `DialogProfileNote` | ❌ | 🗓 BETA OK |
| Character display format (`Char-Acct`) | n/a | **pre-publish checklist item** | **🔧 FIX BEFORE ALPHA** |
| OS keystore (DPAPI/Keychain/libsecret) | ❌ | 🗓 backlog | 🗓 BETA OK (better than Genie 4 already) |

**Profile management rollup:**
- Genie 5 is materially safer than Genie 4 (AES-GCM vs XOR).
- Character display format gap is documented in pre-publish checklist.
- OS keystore is a beta enhancement — current implementation is correct cryptography for local-only storage.

---

## 12. Hands / Vitals / Status Indicators

| Indicator | Genie 4 | Genie 5 | Status |
|---|---|---|---|
| Health bar (0-100%) | ✅ | ✅ | 🚀 SHIP |
| Mana bar | ✅ | ✅ | 🚀 SHIP |
| Spirit / Stamina / Concentration bars | ✅ | ✅ | 🚀 SHIP |
| Bar colors configurable | ✅ via preset | ✅ via DisplaySettings | 🚀 SHIP |
| Roundtime countdown | ✅ ComponentRoundtime | ✅ inline RT badge | 🚀 SHIP |
| RT badge position (command bar vs hands strip) | ❌ | 🆕 toggleable | 🆕 |
| Spell-cast countdown | ❌ | 🆕 magenta bar with (N) prefix | 🆕 |
| Posture: STAND/KNEEL/PRONE/SIT | ✅ | ✅ | 🚀 SHIP |
| Stealth: HIDE | ✅ | ✅ | 🚀 SHIP |
| Stealth: INVISIBLE | ✅ | ⚠️ verify | TBD |
| Afflictions: BLEED / POIS / DIS | ✅ | ✅ | 🚀 SHIP |
| Afflictions: WEB / STUN / JOINED | ✅ | ✅ | 🚀 SHIP |
| Status: DEAD | ✅ | ✅ | 🚀 SHIP |
| Stance: OFF/ADV/FWD/NEU/GRD/DEF | ❌ as badge | 🆕 inline badge | 🆕 |
| Left/Right hand contents | ✅ | ✅ | 🚀 SHIP |
| Prepared spell with elapsed time | ✅ | 🆕 with cast bar | 🆕 |
| `$preparedspell` script variable | ✅ | ✅ | 🚀 SHIP |
| Hands strip position (top/bottom) | bottom only | 🆕 toggleable | 🆕 |

**Hands/Vitals rollup:**
- Full parity on the indicators that matter for gameplay.
- Genie 5 adds: spell cast bar, stance badge, RT badge position toggle, hands strip position toggle.

---

## 13. Other User-Visible Features

| Feature | Genie 4 | Genie 5 | Status |
|---|---|---|---|
| Find / search in current buffer | ✅ Ctrl+F | ❌ | 🗓 BETA OK |
| Paste Multi Line | ✅ | ❌ | 🗓 BETA OK |
| Ctrl+Right-Click selected text → command bar | ❌ | 🆕 (Task #105) | 🆕 |
| Tab-complete script names | ❌ | 🆕 (Task #187) | 🆕 |
| Up-arrow command history | ✅ | ✅ (Task #157 — caret position fix) | 🚀 SHIP |
| `<d cmd>` clickable links | ✅ | ✅ with echoOverride for friendly display | 🆕 |
| `<a href>` URL links | partial | 🆕 (Tasks #168-171) | 🆕 |
| URL safety prompt (`weblinksafety`) | ✅ | ✅ config exists; UI verify needed | 🚀 SHIP |
| User-timeout auto-disconnect | ✅ `usertimeout` | ❌ | 🗓 BETA OK (config exists, not wired) |
| Server-timeout keep-alive | ✅ `servertimeout` | ❌ | 🗓 BETA OK |
| Confirm-on-close dialog | ❌ | 🆕 (Task #148-150) | 🆕 |
| Recording REC indicator | ❌ | 🆕 | 🆕 |
| OBS streamer mode (hide sensitive info) | maybe (unclear) | ❌ | 🎯 v1.0+ |

---

## Compliance Audit

**Per `policy_compliance_review.md` (May 24, 2026)** — Genie 5 today is compliant. The line in DR policy is *responsiveness*, not *automation level*. Click-to-go auto-mapper is fine, scripts are fine, all standard client features are fine.

### Hard nevers — Genie 4 has, Genie 5 must NOT ship

| Feature | Genie 4 status | Genie 5 status | Verification |
|---|---|---|---|
| Auto-reconnect after disconnect | ✅ default ON | 🛑 config key exists, NOT wired to behavior | ✅ verified in `GameConnection.cs` — the retry loop is initial-connect only |
| Auto-walk while window unfocused | possible via script | 🛑 no auto-walk at all yet | ✅ verified (mapper doesn't walk; backlog item to add it with focus-check) |
| Overnight chained travel queues | possible via script | 🛑 no auto-walk infrastructure | ✅ verified |
| Headless / daemon mode | ❌ | 🛑 GUI-only | ✅ verified |
| AI agentive mode (AI drives commands) | n/a (no AI) | 🛑 feature-flagged OFF | ⚠️ `AiAdvisorMode` flag exists; needs architectural wall before any wiring |
| Multi-character orchestration from one instance | ❌ | 🛑 single-session client | ✅ verified |
| Other players' speech to external AI | n/a | ⚠️ AI pipeline shipping OFF; whisper/talk filtering not yet implemented | 🗓 BETA OK (no external AI calls today) |

### Pre-public-release action items (8 from compliance review)

| # | Item | Status |
|---|---|---|
| 1 | AI pipeline gating (default OFF; opt-in checkbox; strip other-players' speech; in-app privacy notice) | 🗓 BETA (today: AI pipeline doesn't make external calls — safe by absence) |
| 2 | Auto-mapper attended detection (halt queue after ~60s window unfocus; Esc cancel; never auto-resume) | 🗓 dependent on auto-walk implementation |
| 3 | Script visible runtime + optional idle-kill (amber after 10 min; user-configurable auto-stop) | 🗓 BETA OK (Script Bar shows running but not runtime) |
| 4 | No auto-reconnect (verified) | ✅ verified |
| 5 | OS keystore for credentials (DPAPI/Keychain/libsecret) | 🗓 BETA OK (AES-GCM is safer than Genie 4 already) |
| 6 | Session recording default OFF in public builds (with visible "recording → {path}" indicator) | ✅ today default OFF; REC indicator visible |
| 7 | In-app policy summary (Help menu) | **🔧 FIX BEFORE ALPHA** (couple of paragraphs; pairs with Help menu work) |
| 8 | Manual verification of unreachable ASP policy pages | 🗓 to do before public broad release; OK for select-few alpha |

### Specific alpha-blocker discovery from this audit

**`GameConnection.cs:85-108`** — the initial-connect retry loop runs up to `MaxReconnectAttempts=10` times with `ReconnectDelayMs=5000ms` between attempts. That's a 50-second worst case if the network is down when the user clicks Connect.

This is NOT a compliance issue (user is at keyboard during connect attempt; dialog shows retry counter). But it's bad UX. **Recommend reducing to 3 attempts** (~15 seconds worst case).

And the `Reconnect` boolean in `GenieConfig.cs:35` (default `true`) is a foot-gun. Round-trips with Genie 4 settings.cfg. Today not wired. A future contributor could wire it. **Recommend either removing the field or adding a comment "MUST NOT be wired — see policy_compliance_review.md hard-nevers #1"**.

---

## Alpha-Blocker Decision Matrix

Items requiring a decision before shipping the alpha to testers. Ordered by severity.

| # | Item | Severity | Cost | Decision needed |
|---|---|---|---|---|
| 1 | **AutoMapper auto-walk** — pre-publish checklist v1 release blocker | High (explicit blocker) | ½ day (helper script) to 1-2 days (engine-driven) | **Ship with click-to-goto only and update ALPHA-README** OR **ship with engine auto-walk** OR **ship with helper script approach** |
| 2 | **`Reconnect` config key foot-gun** — default `true`, not wired, but a future contributor could wire it | Low (no behavior today) | 15 min | **Add `[Obsolete]` + policy comment** OR **remove the key entirely** |
| 3 | **`MaxReconnectAttempts=10`** — 50s wait on bad network | UX, not policy | 10 min | **Reduce to 3** |
| 4 | **Character display format** — pre-publish checklist requires `Character-Account` | Pre-publish hygiene | 30 min | **Ship the change** (title bar, character dropdown, profile picker, defaults) |
| 5 | **Help menu missing** — testers can't easily find Discord/Wiki/GitHub for reporting | Medium (tester orientation) | 30 min for 5 links | **Add basic Help menu with 5 community links** |
| 6 | **In-app policy summary** (compliance pre-publish #7) | Documentation | 30 min | **Add a one-screen Help → DR Policy pane** |

**Total cost to address all 6: ~3 hours** (half a day worst case, assuming auto-walk is decided as helper-script approach).

---

## Recommendations

### Must-do before alpha to select testers (~3-4 hours)

1. **Decide AutoMapper auto-walk approach.** The pre-publish checklist explicitly calls this out as a v1 release blocker. Options:
   - **Option A — Ship with click-to-goto only, document the gap.** Update ALPHA-README "What's NOT working yet" to include "Auto-walk between rooms (click opens goto dialog; you'll need to type or script the route)." Defer to beta. Cost: 5 min wording change.
   - **Option B — Implement engine-driven auto-walk** via `Commands.ProcessInput` + `CommandQueue` for RT gating. Cost: 1-2 days. Risk: needs window-focus check + cancel-on-input + visible queue indicator per compliance review #2.
   - **Option C — Ship Mapper Helper Script approach**: a community-style `.cmd` script that walks the route. Cost: ½ day. Risk: limited by users' script-engine experience.
   - **Recommendation: Option A for select-few alpha; defer auto-walk to beta. Tester feedback will inform B vs C decision.**

2. **Address the `Reconnect` foot-gun.** Either remove `Reconnect` from `GenieConfig.cs` and the load/save handlers, or annotate with `[Obsolete]` + a comment pointing to `policy_compliance_review.md`. Cost: 15 min.

3. **Reduce `MaxReconnectAttempts` to 3 (with 3s delay).** Worst-case 9s wait on initial connect failure vs current 50s. Cost: 10 min.

4. **Roll out `Character-Account` display format.** Title bar, character dropdown, profile picker, default profile name. Cost: 30 min. Per pre-publish checklist.

5. **Add basic Help menu** with 5 links: Discord, Wiki (Elanthipedia), GitHub repo, DR policy page (play.net), and "Report Issue" (mailto: or GitHub Issues URL). Plus one extra item "DR Policy Summary..." that opens an in-app one-pager covering the compliance posture (hard-nevers list, Genie 5's compliance approach). Cost: 30 min.

### Should-do before alpha if time allows (~half-day)

6. **Reduce verbose ALPHA-README "What's NOT working yet" section to match this audit.** Currently lists "No plugin host", "No auto-update", "No JavaScript script support", "No themes", "No workspace presets", "Configuration dialog rough edges." Could add: "No Find/Search (Ctrl+F)", "No Paste Multi-Line", "No master toggle for trigger/highlight/etc. engines", "No Auto Log".

7. **Stub out the missing master toggles** (Triggers Enabled, Gags/Ignores Enabled, AutoMapper Enabled, Images Enabled). Even non-functional, having them in the File menu sets expectations correctly. Cost: 30 min.

### Defer to beta (no changes needed before alpha)

These are documented gaps that testers will encounter and report on, which is fine for an alpha:
- Auto Log (text logging) — ~80 LOC, backlog
- Raw XML Window — half-day, backlog
- Layout save/load (workspace presets) — 1-2 days, backlog
- Configuration dialog UX pass — half-day, backlog
- UI Themes (Light/Dark) — day+, backlog
- Familiar/Death/Active Spells stream tabs — small per tab, backlog
- Per-tag visibility on rule engines (master toggle Triggers/Gags/Highlights) — half-day total
- Mapper auto-walk (any approach) — see Option B/C above
- Audio support + image rendering — multi-day each
- `Always On Top` window option — 5 LOC, trivial
- Find / Paste Multi-Line — small Avalonia features

### Defer to v1.0+ (vision items)

- Plugin host + marketplace
- LAMP 2.0 updater
- JavaScript `.js` script support (Jint)
- AI advisor mode (with compliance gating)
- Cloud sync / cross-device profiles
- Workspace presets ("Combat layout" / "Healing layout")
- Combat analytics (DPS / debuff uptime)
- Visual trigger/flow designer
- Embedded browser / wiki integration

### Never (DR policy)

- Auto-reconnect after disconnect (the `Reconnect` config key)
- Agentive AI (AI driving `Commands.ProcessInput`)
- Auto-walk while window unfocused
- Headless / daemon mode
- Multi-character orchestration from one instance
- Shipping other players' speech to external AI

---

## Closing notes

**Genie 5 is ready to ship to select-few alpha testers** once the 6 items in the Alpha-Blocker Decision Matrix are addressed (or explicitly accepted as gaps in ALPHA-README). The largest gap by feature surface is the absent plugin host — this is intentional and on the v1.0+ roadmap.

**The most consequential gap is AutoMapper auto-walk** (pre-publish checklist v1 blocker). Recommended path: ship the alpha with click-to-goto only, document the gap clearly in ALPHA-README, gather tester feedback on which auto-walk approach (engine vs script) they'd prefer.

**Compliance posture is clean.** No forbidden features ship today. The two compliance-adjacent risks are well-contained:
- `Reconnect` config key exists but isn't wired (foot-gun, not bug)
- AI pipeline scaffolding exists but doesn't make external calls

Both will need attention as beta work matures, but neither blocks alpha.

**Trigger phrase to revisit this doc:** "review the Genie 4 vs Genie 5 comparison" or "what's in the comparison audit."
