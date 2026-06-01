# Policy Compliance

Genie 5 is third-party software for [DragonRealms](https://www.play.net/dr), and it's designed to live comfortably within Simutronics' [Allowed Software policy](https://elanthipedia.play.net/DragonRealms_Policy:_Allowed_Software). This page explains the line the project holds and why — useful whether you're a player wondering "is this okay?" or a contributor weighing a feature.

## The principle

Simutronics permits third-party clients but draws a hard line at software that **plays the game for you**. The community shorthand is:

> **Responsive software is fine; agentive software is not.**

You must be present, attending to the session, and in control of decisions that affect your character. Genie 5 is built to *amplify an attentive player*, not to replace one.

## Five hard "nevers"

These will not ship, and contributions that add them are declined:

1. **No auto-reconnect.** If the connection drops, Genie tells you and stops. You reconnect by hand. (An auto-reconnect that resumes a long script after a drop is the classic "playing while away" pattern.)
2. **No agentive AI.** Any AI features are **advisor-only** — they surface text you read; they never drive game commands. See [AI Advisor](AI-Advisor).
3. **No auto-walk while unfocused.** The mapper's click-to-walk pauses automatically when the Genie window loses focus, and cancels on Esc, on typed input, or on disconnect. It never auto-resumes across a disconnect.
4. **No headless / daemon mode.** Genie requires its visible window. There is no flag to run it without a UI.
5. **No shipping other players' speech to external services.** If the optional AI ever sends context to an external service, other players' whispers/speech/thoughts/tells are stripped first, and it's opt-in behind a disclosure.

Two more follow from the same principle: **no multi-character orchestration** from one client, and **no overnight chained travel queues**.

## What Genie 5 *does* ship

The line is automation that's *responsive to your input*, not automation that *replaces* it:

- **Click-to-walk** — a click is direct intent; the step-by-step walker is responsive to it, and Esc / typed commands / losing focus all interrupt it cleanly. See [The Mapper](Mapper).
- **`.cmd` scripts** — faithful to Genie 4. Scripts you consciously start are responsive to your intent; trigger-started scripts follow the same rules Genie 4's trigger system always did. See [Scripting](Scripting).
- **Triggers, aliases, highlights, substitutes, gags, macros** — pattern-driven *responses* to game text, not independent agents. See [Configuration & Rules](Configuration).
- **Cross-zone routing** — the pathfinder finds a route; the walker executes it under the same attended-mode gates as single-zone walking, and a boat countdown is a UI hint, not an actor. See [Cross-Zone Travel](Cross-Zone-Travel).

## Recording & credentials

- **Session recording** is **off by default**, shows a visible 🔴 REC indicator when on, and writes only to your machine.
- **Passwords** are stored locally, encrypted with **AES-256-GCM**, and sent only to Simutronics' official authentication servers.

## If you're unsure about a feature

Open an issue with the `policy-question` label *before* writing the PR (or ask in the Discord `policy-questions` channel). The useful framing: describe what the user does, what the app does in response, and whether the user is at the keyboard when each step happens. If "the user isn't at the keyboard" appears anywhere in the flow, the feature probably needs a rethink.

The full developer-facing compliance review is [POLICY.md](https://github.com/GenieClient/Genie5/blob/main/docs/POLICY.md).

## Related

- [AI Advisor](AI-Advisor) — the advisor-only design and its privacy gates.
- [The Mapper](Mapper) — attended-mode walking.
- [Connecting & Profiles](Connecting) — no auto-reconnect, encrypted credentials.
