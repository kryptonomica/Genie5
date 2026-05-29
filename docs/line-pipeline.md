# Line Pipeline

This page documents what happens to a unit of game output between the [DrXmlParser](../src/Genie.Core/DrXmlParser.cs) emitting a `GameEvent` and that event reaching the UI, scripts, game-state, and rule engines. Most "the line didn't appear where I expected" / "my trigger didn't fire" questions are answered by getting this fan-out right.

Unlike Genie 4 (and the earlier Kzin prototype), there is **no single `LineReceived` handler**. Genie 5 is event-driven: the parser publishes typed events on a [Reactive](https://github.com/dotnet/reactive) `IObservable<GameEvent>`, and each consumer subscribes to exactly the event types it cares about. The "order of operations" is therefore the set of subscriptions wired in [GenieCore](../src/Genie.Core/GenieCore.cs), plus the UI-side render path in [GameTextViewModel](../src/Genie.App/ViewModels/GameTextViewModel.cs).

## End-to-end view

```
GameConnection.RawXmlStream  (hot IObservable<string>)
    │
    ├─► DrXmlParser.Feed → GameEvents (IObservable<GameEvent>)
    │        │
    │        ├─► GameStateEngine.Apply          live GameState snapshot (vitals, room, RT, statuses)
    │        ├─► ScriptGlobalsSync.OnEvent       mirror state into Scripts.Globals ($health, $north, …)
    │        ├─► GenieCore._gameEventSub:
    │        │       TextEvent   → Scripts.OnGameLine + Triggers.ProcessLine
    │        │       PromptEvent → Scripts.OnPrompt
    │        │       NavEvent    → Scripts.OnRoomChanged
    │        ├─► MapperGameStateAdapter           feeds AutoMapperEngine.OnStateChanged
    │        └─► GameTextViewModel (UI):
    │                TextEvent(stream=="main") → Substitutes → Gags → highlight render
    │                side-stream TextEvents     → their own stream VMs / tabs
    │
    └─► AiRawStream (toggleable) → AiContextBuffer   (never blocks the parser)
```

The two engine-side state consumers — `GameStateEngine` and `ScriptGlobalsSync` — each subscribe **independently** to `GameEvents` in the `GenieCore` constructor ([GenieCore.cs:183](../src/Genie.Core/GenieCore.cs#L183), [GenieCore.cs:274](../src/Genie.Core/GenieCore.cs#L274)). Rx delivers events to subscribers in subscription order, and both are wired before the `_gameEventSub` that drives scripts/triggers. That ordering is load-bearing — see below.

## Why the ordering matters

- **State and globals are applied before scripts see the line.** `GameStateEngine` and `ScriptGlobalsSync` subscribe before `_gameEventSub`. So by the time `Scripts.OnGameLine(text)` runs for a `TextEvent`, `$webbed`, `$health`, `$roomexits` etc. already reflect any indicator/vital/compass events that arrived earlier in the same burst. A script's `matchwait`/`waiteval` evaluates against current state, not last line's.
- **Scripts run before triggers, both on every `TextEvent`.** `_gameEventSub` calls `Scripts.OnGameLine` then `Triggers.ProcessLine` ([GenieCore.cs:286-289](../src/Genie.Core/GenieCore.cs#L286)). Scripts get first crack at a line (to satisfy a pending `matchwait`); triggers fire their command actions after.
- **`PromptEvent` advances RT-gated scripts.** A prompt is the natural unblock for `wait`, decrements in-flight type-ahead, and lets the roundtime gate re-check. The DR server only prompts in response to commands — see the [scripting engine's roundtime gate](scripting-engine.md#the-roundtime-gate).
- **`NavEvent` unblocks `move`.** A new server room id is what releases a script blocked on `move`/`nextroom`.
- **The AI pipe is a separate tap on raw XML**, gated by `AiPipeEnabled`, and is structured so it can never block the parser or the game.

## Engine-side consumers

### GameStateEngine — the live snapshot

[GameStateEngine](../src/Genie.Core/GameStateEngine.cs) is the single source of truth for "what is the character doing right now." It matches on event type and writes into the shared [GameState](../src/Genie.Core/GameState.cs):

- `ProgressBarEvent` → `Vitals.*`
- `ComponentEvent` → `Room.*` (title/desc/exits/objs/players), `Combat.Stance`, `CharacterName`; `exp <skill>` → `LiveSkills`
- `RoundTimeEvent`/`CastTimeEvent` → `Combat.RoundTimeEnd`/`CastTimeEnd`
- `IndicatorEvent` → adds/removes a `CharacterStatus` in `ActiveStatuses`
- `HeldItemEvent` → `Inventory.LeftHand`/`RightHand` (+ exist ids)
- `SpellEvent` → `Combat.PreparedSpell`, `NavEvent` → `Room.RoomId`, `CompassEvent` → `Room.CompassExits`

The UI binds to `GameState`; scripts read it indirectly through globals; the mapper reads it through an adapter.

### ScriptGlobalsSync — Genie 4 reserved variables

[ScriptGlobalsSync](../src/Genie.Core/Scripting/ScriptGlobalsSync.cs) mirrors state into `Scripts.Globals` so community scripts can read the Genie 4 vocabulary: `$health`, `$mana`, `$stamina`/`$fatigue`, `$righthand`/`$righthandnoun`/`$righthandid`, `$preparedspell`, `$stance`, the status flags (`$standing`, `$webbed`, …), the per-exit booleans (`$north`, `$up`, …), and the room fields (`$roomname`, `$roomdesc`, `$roomexits`, `$gameroomid`). It uses **per-event-type dispatch** (each callback touches only the 1–12 variables relevant to that event) and writes into a `ConcurrentDictionary` so the parser thread and script reads don't need a lock. Defaults are seeded at construction so a script launched the instant after connect sees usable values.

## UI render path — GameTextViewModel

The user-facing rule pipeline lives in [GameTextViewModel.Attach](../src/Genie.App/ViewModels/GameTextViewModel.cs#L26). For each `TextEvent` on the `main` stream, observed on the UI thread:

1. **Display filter** — skip if **Window → Game Text** is toggled off (`DisplaySettings.ShowGameText`).
2. **Substitutes** — `core.Substitutes.Apply(text)` rewrites the text. Genie 4 ordering: substitute first, then gag.
3. **Gags** — `core.Gags.ShouldGag(text)` drops the whole line if any enabled rule matches.
4. **Span carry** — link and bold spans are kept **only if no substitute fired** (`ReferenceEquals(text, e.Text)`). A substitution shifts character offsets, so spans are dropped rather than remapped — clickable text is a UX bonus, not a correctness requirement.
5. **Render** — `AddLine` appends a `TextLine`. Highlighting is applied lazily by `TextLine.Inlines`, which tokenizes via [DefaultHighlights](../src/Genie.App/Highlighting/DefaultHighlights.cs) (user rules + link/bold spans). The buffer is capped at `MaxLines = 2000`.

Echoes (typed commands, `#echo`, `[script]`/`[recorder]` diagnostics) arrive on the `EchoLine` event, not as `TextEvent`s. They render with the `System` colour and are gated by `ShowEchoText` (bare) or `ShowScriptText` (bracketed `[tag]` lines).

### Live re-highlighting

When the user adds or edits a highlight rule, `UserHighlights.RulesChanged` fires and `RetokenizeAllLines` replaces every existing `TextLine` with a fresh instance so already-rendered text repaints — not just future lines.

### Side streams

`TextEvent`s on non-`main` streams (`logons`, `talk`, `whispers`, `thoughts`, `familiar`, …) route to their own stream view-models / dock tabs. When a stream's panel is closed, its lines can be folded back into the main window via `AddStreamLine`, prefixed with `[stream]`. Side-stream lines do not run the trigger pass — triggers are a main-window concern (a trigger that runs `put …` shouldn't fire on a whisper).

## Class gating

[ClassEngine](../src/Genie.Core/Classes/ClassEngine.cs) holds boolean classes the user toggles. Triggers, highlights, substitutes, and gags can each be scoped to a class; if the class is off, the rule is skipped at evaluation time. State is live — toggling a class affects every associated rule on the next line. This is wired by assigning `Classes` onto each engine in the `GenieCore` constructor.

## Command path (the other direction)

User input flows the opposite way, through [CommandEngine](../src/Genie.Core/Commanding/CommandEngine.cs) via `GenieCore.ProcessInput`:

```
ProcessInput(text)
  → alias expansion + separator split (`;`)
  → #cmd routing (#var, #trigger, #echo, #class, … — see CommandEngine)
  → ICommandHost.SendToGame → local echo (EchoLine) + AutoMapper.OnCommandSent + socket
```

`AutoMapper.OnCommandSent` lets the mapper observe outgoing movement verbs so it can correlate the next room change with the direction you moved. Note there is **no** `#goto`/`#travel`/`#mapper` meta-command — walking is driven from the Mapper UI ([AutoWalkService](../src/Genie.App/Services/AutoWalkService.cs)); see [mapper.md](mapper.md).

## Diagnostics

- **Window → Game Text / Echo Lines / Script Lines** toggle what's rendered.
- **File → Record Session (raw XML)** captures the verbatim stream for replay.
- Trace/debug logging on `DrXmlParser` and `GameStateEngine` surfaces unknown tags.

## Code references

- **[GenieCore.cs](../src/Genie.Core/GenieCore.cs)** — all engine-side subscriptions; the `_gameEventSub` fan-out; the command host.
- **[GameStateEngine.cs](../src/Genie.Core/GameStateEngine.cs)** — events → live `GameState`.
- **[ScriptGlobalsSync.cs](../src/Genie.Core/Scripting/ScriptGlobalsSync.cs)** — events → script globals.
- **[GameTextViewModel.cs](../src/Genie.App/ViewModels/GameTextViewModel.cs)** — UI substitute → gag → highlight render path.
- **[SubstituteEngine.cs](../src/Genie.Core/Substitutes/SubstituteEngine.cs)**, **[GagEngine.cs](../src/Genie.Core/Gags/GagEngine.cs)**, **[HighlightEngine.cs](../src/Genie.Core/Highlights/HighlightEngine.cs)**, **[NameHighlightEngine.cs](../src/Genie.Core/Highlights/NameHighlightEngine.cs)** — user-rule engines.
- **[TriggerEngineFinal.cs](../src/Genie.Core/Triggers/TriggerEngineFinal.cs)** — command-firing triggers.
- **[CommandEngine.cs](../src/Genie.Core/Commanding/CommandEngine.cs)** — input routing and `#cmd` dispatch.
