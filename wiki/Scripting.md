# Scripting

Genie 5 runs Genie 4 `.cmd` scripts. The engine is a **faithful port** of Genie 4's Wizard-derived script language — if you've written scripts for Genie 4, Wizard, or StormFront, the syntax is identical and your scripts should just run. New to scripting? This page is a friendly tour; the complete vocabulary is on [Scripting Reference](Scripting-Reference).

> If a script worked in Genie 4 and doesn't work here, that's a bug we want to hear about — [file an issue](https://github.com/GenieClient/Genie5/issues/new). Script compatibility is treated as non-negotiable.

## Where scripts live

Drop `.cmd` files into your **Scripts** folder:

- Per character: `Profiles/<Character>-<Account>/Scripts/`
- Shared (before you've logged in): `Scripts/`

See [Application Folders](Application-Folders) for the exact path on your OS. No restart needed — Genie picks up new files immediately.

## Hello world

Create `Scripts/hello.cmd`:

```
echo Hello, %1!
```

Run it from the command bar (scripts are prefixed with `.`):

```
.hello world
```

Output: `Hello, world!` — `%1` was filled in by the argument you passed.

## The vocabulary at a glance

```
# A comment is a # followed by whitespace or end-of-line.
# Lines starting with #command are meta-commands (#echo, #put, #stop, ...).

# Variables:
#   $name  reads a global / live game-state value
#   %1, %2 are the arguments passed to the script (%0 is all of them)
#   var foo = bar   sets a local variable; read it back as %foo

# Send a command to the game:
put look

# Wait for game text matching a label (literal) or regex:
match Done You finish searching.
matchwait

# Or block until a substring appears:
waitfor You can move again

# Pause for seconds:
pause 2.5
waitpause            # sleep until the current roundtime expires

# Conditionals on live game state:
if_health < 50 then put cast 1101
if def(weapon) then echo I have a weapon set

# Loops via labels + goto:
LOOP:
  put assess
  pause 1
  goto LOOP
```

## Roundtime safety

Scripts are **roundtime-aware**. By default, a `put` issued while you're in roundtime queues, waits, and respects the type-ahead budget — you don't have to write `waitpause; put north` everywhere. Use `waitpause` explicitly only when you need to *gate* on roundtime expiring before the script proceeds (for example, before computing a value that depends on being able to act).

Because DragonRealms doesn't announce when roundtime ends, the engine schedules its own wake-up from the live roundtime clock — so RT-gated scripts resume correctly. The mechanics are in [Scripting Reference](Scripting-Reference#the-roundtime-gate).

## Game-state variables

Every live game-state field is exposed as a `$variable`, so scripts can read your character's condition directly. Common ones:

| Variable | Holds |
| --- | --- |
| `$health`, `$mana`, `$spirit`, `$concentration`, `$stamina` | Current vital percentages (0–100). |
| `$roomname`, `$roomdesc`, `$roomexits` | Current room info. |
| `$righthand`, `$lefthand` | What you're holding. |
| `$preparedspell` | Prepared spell (or empty). |
| `$stance` | `off` / `adv` / `fwd` / `neu` / `grd` / `def`. |
| `$kneeling`, `$prone`, `$sitting`, `$stunned`, `$hidden`, `$webbed`, … | Status booleans. |
| `$roundtime` | Seconds of roundtime remaining. |

Type `#vars` at the command bar to see the full live list. The complete table is on [Scripting Reference](Scripting-Reference#engine-set-globals).

## Running and stopping scripts

```
.myscript arg1 arg2   # run Scripts/myscript.cmd with %1=arg1 %2=arg2
#scripts              # list running scripts
#stop myscript        # stop one script
#stopall              # stop everything
#edit myscript        # open it in your editor (set via Display Settings)
```

A **Script Bar** above the command bar shows what's running, with stop/edit controls; it hides itself when nothing is running.

## What's different from Genie 4

A few intentional divergences:

- **Per-character script folders** — scripts saved while playing one character live separately from another's. The first-launch migration copies your existing Genie 4 `Scripts/` across once.
- **Undefined `$var` aborts the script** — rather than silently expanding to empty (a classic Genie 4 bug source), Genie 5 stops with a clear reason. Use `if def(name)` to test first.
- **Comments** — `#` is a comment only when followed by whitespace or end-of-line. `#put north` is a meta-command, not a comment.
- **`gosub` for reusable routines** — jumping into a nested/indented block isn't reliable; use `gosub` for sub-routines.

## Example scripts to study

The community [DR-Genie-Scripts](https://github.com/Tirost/DR-Genie-Scripts) repo has ~55 real scripts, from one-liners to 500+-line hunt loops. Start with the simpler ones (foraging, alchemy, simple buffers) before combat scripts.

## Related

- [Scripting Reference](Scripting-Reference) — the complete language: every statement, variable scoping, the roundtime gate, type-ahead.
- [Configuration & Rules](Configuration) — triggers and variables that scripts build on.
- [Lich 5 Integration](Lich-5-Integration) — running Ruby scripts alongside `.cmd`.
