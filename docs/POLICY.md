# DragonRealms policy compliance

Genie 5 is a third-party client for [DragonRealms](https://www.play.net/dr),
Simutronics' text-based MMO. This document captures the explicit boundaries
the project commits to staying within — both the absolutes ("never ship X")
and the design choices that keep automation on the responsive side of the
line.

If you're considering a feature that touches automation, AI, notifications,
or anything that runs while the user isn't actively at the keyboard, **read
this file first** and open an issue with the policy question if anything is
unclear.

## The Allowed Software policy in practice

Simutronics permits third-party clients but draws a hard line at software
that plays the game for the user. The community phrasing is *"responsive
software is fine; agentive software is not."* The user must be present,
attending to the session, and in control of decisions that affect the
character's state.

Five hard nevers fall directly out of that:

### 1. No auto-reconnect after disconnect

If the game socket drops, Genie 5 surfaces the disconnect and stops. The
user reconnects by hand. A `Reconnect=true` config key exists for Genie 4
config-file compatibility but **is not wired to behavior** in Genie 5; the
reconnect loop in `GameConnection.cs` only retries during the *initial*
connect attempt, never after a session has been established.

Why this matters: an auto-reconnect that resumes a multi-hour script
after a disconnect is the classic "playing while away" pattern. We won't
ship it.

### 2. No agentive AI control

The AI integration in `Genie.Core.AI.AiContextBuffer` reads the game stream
and produces text suggestions. It does **not** and **must not** call
`Commands.ProcessInput` directly or by any indirect path. AI responses are
displayed to the user, who decides what to type.

This is enforced as a release gate (G4) in `AiContextBuffer.cs` and any PR
that wires AI responses back into the command pipeline will be closed.

### 3. No auto-walk while the window is unfocused

The AutoMapper's click-to-walk feature pauses the walk automatically after
60 seconds of window unfocus (`AutoWalkService.OnWindowDeactivated`). Esc
cancels the walk; any typed command cancels the walk; the unfocus timer
cannot be extended.

The pause never auto-resumes across a disconnect — a new click is required
after reconnection.

### 4. No headless / daemon mode

Genie 5 requires an interactive Avalonia window. There is no `--headless`,
no `--service`, no command-line flag that starts the app without UI. This
is enforced architecturally — the app's startup path in `Program.cs` only
constructs an `AppBuilder` configured for desktop runtime.

### 5. No shipping other players' speech to external services

The `AiContextBuffer` is the only component that sends game-text content
off-machine, and it's:

- **Default OFF.** Opt-in checkbox in Settings with a one-page privacy
  disclosure (release gate G1).
- **Stripping other-player content before any external send** is a
  pre-release requirement (release gate G2): the buffer captures whisper
  / speech preset content and `pushStream` blocks from `talk`, `whispers`,
  `thoughts`, `familiar` — public-area player utterances that other
  players have not consented to having forwarded.
- **In-character advisor mode** stays feature-flagged off entirely until
  there's an explicit legal read on perpetual-license clauses around
  player content (release gate G3).

Recordings (`File → Record Session…`) write raw XML to a per-machine
folder under `%APPDATA%/Genie5/Logs/` and never leave the developer's
machine.

## What we DO ship

The line is automation that's responsive to user input, not automation
that replaces user input.

- **Click-to-walk** via the AutoMapper: a click is direct user intent;
  the step-by-step walker is *responsive* to that intent. Esc / typed
  commands / window unfocus all interrupt cleanly.
- **`.cmd` scripts:** the script engine is faithful to Genie 4's
  interpreter. Scripts that worked in Genie 4 work here. Scripts you
  consciously start are responsive to your intent; scripts that auto-start
  on triggers fall under the same rules as Genie 4's trigger system.
- **Triggers, aliases, highlights, substitutes, gags, macros:** all
  carry over from Genie 4. These are pattern-driven *responses* to game
  text, not independent agents.
- **Cross-zone routing:** the Dijkstra pathfinder finds a route; the
  walker executes it step-by-step under the same compliance gates as
  single-zone walking. A boat-wait countdown is a UI hint, not an
  automation actor.

## If you're unsure about a feature

Open an issue with the `policy-question` label *before* writing the PR.
The pattern that's worked: describe what the user does, what the app
does in response, and whether the user is at the keyboard when each step
happens. If "the user isn't at the keyboard" appears anywhere in the
flow, the feature probably needs to be redesigned or dropped.

The `policy-questions` channel in our Discord is a faster, lower-friction
place to sanity-check an idea before formalizing it as an issue.
