# JavaScript Scripting

Genie 5 runs JavaScript on the pure-managed [Jint](https://github.com/sebastienros/jint) engine — no native dependencies, identical on Windows, macOS, and Linux. There are **two ways** to use it:

1. **Standalone `.js` scripts** — run a whole `.js` file as a script, like a `.cmd`. Good for procedural automation with real blocking waits (`waitFor`, `pause`, hunt loops).
2. **Function libraries called from a `.cmd`** — keep a library of functions in a `.js`, `include` it from a `.cmd`, and call them with `js` / `jscall`. This is the Genie 4 "array script" style, ideal for list/data work where `.cmd` variables are clumsy.

For the `.cmd` language itself, see **[Scripting](Scripting)** and the **[Scripting Reference](Scripting-Reference)**.

---

## 1. Standalone `.js` scripts

Drop a `.js` file in your [scripts folder](Application-Folders) and run it by name, exactly like a `.cmd`:

```
.myscript
.myscript Kragh 3        # %0 = "Kragh 3", args are genie.getVar("1") = "Kragh", "2" = "3"
```

The script gets a `genie` object (aliased `game`) with the API below. Each `.js` runs on its own thread, so blocking calls really block — write straight-line procedural code.

### Sending commands

```javascript
genie.put("stance defensive");   // send a command to the game
genie.send("forage for herbs");  // send() is an alias for put()
```

### Output

```javascript
genie.echo("ready to hunt");                    // a line to the main window
genie.echoTo("Spotter", "target sighted", "red"); // to a named window, in colour
```

### Waiting on game text

```javascript
genie.put("go gate");
genie.waitFor("You stride through");        // block until a line contains this (case-insensitive)

var m = genie.waitForRe("You have (\\d+) silver"); // regex; returns capture groups
if (m.length > 1) genie.echo("Silver: " + m[1]);

var hit = genie.matchWait(["You hit", "You miss", "is parried"]); // first of several
genie.echo("result: " + hit);

genie.waitForPrompt();   // block until the next game prompt
```

### Pausing & roundtime

```javascript
genie.pause(2);                      // sleep 2 seconds
while (genie.roundtime() > 0)         // seconds of roundtime left
    genie.pause(0.2);
```

### Variables (standalone)

In a standalone `.js`, `getVar`/`setVar` are the **script's own** locals/arguments; `get`/`set` read and write **`$globals`** — the place to share data with `.cmd` scripts and live game state.

```javascript
var who = genie.getVar("1");     // first argument (%1)
genie.set("lasttarget", who);     // writes $lasttarget — visible to .cmd as $lasttarget
var hp = genie.get("health");     // reads a live game-state global ($health)
```

### Timers & stopping

```javascript
genie.timerStart();
// … do work …
genie.echo("took " + genie.timerElapsed() + "s");
genie.timerStop();

if (genie.get("health") < 20) genie.stop();   // end this script
```

### Safety guards

Standalone `.js` scripts may run for hours, so there's **no wall-clock limit** — but a memory cap (128 MB) and a runaway-loop guard (a tight loop with *no* `genie.*` call is aborted) keep a buggy script from pegging a core or exhausting memory. If you hit the runaway guard, add a `genie.pause` / `genie.waitFor` inside the loop.

---

## 2. Function libraries from a `.cmd` (`include` + `js` / `jscall`)

Keep reusable functions in a `.js`, pull them into a `.cmd`, and call them. The functions read and write the **calling `.cmd`'s variables**, so JavaScript does the data/logic work while the `.cmd` drives the game.

### `include` a library

```
# in your .cmd
include arrays.js          # loads the function definitions for this script run
```

### `js` and `jscall`

```
js doSort("loot", 0)                 # run a function, ignore the result
jscall count routeLength("route")    # run a function, store its return in %count
echo There are %count steps.
```

- **`js <expr>`** evaluates the expression (return value discarded).
- **`jscall <var> <expr>`** evaluates and stores the result string in `%var`.
- The expression is passed **raw** (not `%`/`$`-substituted), so a library can resolve `%`/`$` itself (e.g. `findIndex("list", "%target")` where the function does its own lookup).

### Variables (libraries)

Inside an included library, use the **bare** functions — they map to the calling `.cmd`'s scope:

| Function | Reads / writes |
|---|---|
| `getVar(name)` / `setVar(name, value)` | the `.cmd`'s **`%`variables** |
| `getGlobal(name)` / `setGlobal(name, value)` | **`$`globals** (shared, live game-state) |
| `echo(text)` | a line to the game window |
| `put(cmd)` / `send(cmd)` | send a command to the game |

```javascript
// arrays.js — a tiny library
function routeLength(name) {
    return getVar(name).toString().split('|').length;   // reads the .cmd's %name
}
function pushStep(name, step) {
    var v = getVar(name).toString();
    setVar(name, v.length === 0 ? step : v + '|' + step); // writes it back
}
```

```
# driver.cmd
include arrays.js
var route n|n|e
js pushStep("route", "go gate")
jscall len routeLength("route")
echo route=%route  len=%len           # route=n|n|e|go gate  len=4
```

### Division of labour (recommended pattern)

Let **JavaScript do the logic** and the **`.cmd` do the paced movement** — the `.cmd` `put`s each step and waits out roundtime, so the walker stays responsive. Here's the pattern — a `travelutil.js` helper plus a `.cmd` driver:

```javascript
// travelutil.js — returns the next step and advances an index; "" when done
function nextStep(routevar, idxvar) {
    var list = getVar(routevar).toString().split('|');
    var i = parseInt(getVar(idxvar), 10);
    if (isNaN(i) || i < 0) i = 0;
    if (i >= list.length) return '';
    setVar(idxvar, (i + 1).toString());
    return list[i];
}
```

```
# walk a route, paced by the .cmd (not JS)
include travelutil.js
var route n|n|e|go gate|w
var i 0
walk:
  jscall dir nextStep("route", "i")
  if "%dir" = "" then goto done
  put %dir
  pause 1                # or: waitfor the room prompt; honours roundtime
  goto walk
done:
  echo Arrived.
```

### Limits

- A library's JavaScript context lives **within one running `.cmd`** — `include`/`js`/`jscall` share state only inside that script, not across separate command-bar lines.
- `js` / `jscall` run **synchronously and time-bounded** on the script thread (they're for quick logic, not hunt loops). Blocking waits (`waitFor`) belong in a **standalone `.js`**, not a `js`/`jscall` call.
- No host / filesystem access by default.

---

## Porting Genie 4 `.js` libraries

Genie 4 ran a much older Jint (0.8.8); Genie 5 runs a current, spec-compliant Jint. Two idioms differ — both are easy:

1. **`array.length()` → `array.length`.** In modern JavaScript `length` is a *property*, not a method. **Genie 5 auto-converts `.length()` to `.length` when you `include` a library**, so existing Genie 4 array libraries load and run unchanged. For *new* code, write `array.length` (no parentheses).

2. **`localeCompare(...) == 1` / `== -1` → `> 0` / `< 0`** (optional). The spec only guarantees the *sign* of the result; comparing by sign is robust on any engine. (Genie 5's engine does return ±1, so existing comparisons still work — this is just future-proofing.)

Before / after:

```javascript
// Genie 4 style                     // portable
for (i = 0; i < list.length() - 1; i++) {   for (i = 0; i < list.length - 1; i++) {
    if (a.localeCompare(b) == 1) { … }           if (a.localeCompare(b) > 0) { … }
```

Everything else (`split`, `join`, `slice`, `getVar`/`setVar`/`getGlobal`, `switch`, `for`) carries over unchanged.

---

## Related

- **[Scripting](Scripting)** — the `.cmd` language guide
- **[Scripting Reference](Scripting-Reference)** — every command, variable, and operator
- **[Application Folders](Application-Folders)** — where scripts live
- **[Policy & Compliance](Policy-Compliance)** — automation is fine when you stay responsive; the same rules apply to `.js`
