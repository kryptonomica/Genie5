# DragonRealms XML Protocol

Once [SGE auth](SGE_PROTOCOL.md) hands off and the FE handshake completes, the DragonRealms game server streams a continuous flow of **XML-ish markup interleaved with bare text**. This page documents that stream, the tag vocabulary Genie 5 recognises, and how [DrXmlParser](../src/Genie.Core/DrXmlParser.cs) turns it into strongly-typed [GameEvent](../src/Genie.Core/GameEvents.cs) objects.

For the login handshake that precedes any of this — the `eaccess.play.net` flow, the FE identifier, StormFront vs. Wizard mode — see [SGE_PROTOCOL.md](SGE_PROTOCOL.md). This page picks up after the socket is live.

## What the stream is (and isn't)

DR's wire format looks like XML but is **not a well-formed document**:

- There is no root element. Tags arrive in a stream, sometimes nested, sometimes self-closed.
- A single logical element can be split across socket reads at any byte boundary.
- Bare text (room descriptions, combat, speech) is interleaved freely between tags.
- Some "tags" the standard `XmlReader` rejects outright — most notably a bare `<d>` with no attributes.

The parser's job is to consume whatever arrives, recover from malformations, and emit a clean event for each thing that happened. It deliberately does **not** try to build a DOM.

## Pipeline position

```
GameConnection           raw socket bytes, split into chunks at '>' boundaries
    │  RawXmlStream (hot IObservable<string>)
    ▼
DrXmlParser.Feed(chunk)  accumulate → ProcessBuffer → ParseTag / AccumulateText
    │  GameEvents (Subject<GameEvent>)
    ▼
GenieCore subscribers    GameStateEngine · ScriptGlobalsSync · Scripts · Triggers
                         GameTextViewModel (UI)
```

[GenieCore](../src/Genie.Core/GenieCore.cs) wires `_connection.RawXmlStream.Subscribe(_parser.Feed)` ([GenieCore.cs:187](../src/Genie.Core/GenieCore.cs#L187)). Everything downstream consumes `GameEvents`, never the raw stream (except the optional AI pipe and the Session Recorder, which tap raw XML directly).

## How parsing works

[DrXmlParser](../src/Genie.Core/DrXmlParser.cs) is a streaming character scanner, not a DOM parser. The hot path in `ProcessBuffer` ([DrXmlParser.cs:109](../src/Genie.Core/DrXmlParser.cs#L109)):

1. Append the incoming chunk to `_rawBuffer`.
2. Find the next `<`.
   - Text before it → `AccumulateText`.
   - No `<` at all → it's all text; accumulate and return.
3. At a `<`, find the matching `>`.
   - No `>` yet → the tag is split across reads; **return and wait** for more data. This is how mid-tag TCP splits are handled — the partial tag simply stays in `_rawBuffer`.
   - Otherwise slice out the full tag and hand it to `ParseTag`.

### Tag parsing with a fallback

`ParseTag` ([DrXmlParser.cs:227](../src/Genie.Core/DrXmlParser.cs#L227)) handles three cases:

- **End tags** (`</component>`) — `XmlReader` in fragment mode can't read standalone end tags, so the name is scraped directly and dispatched to `HandleEndElement`.
- **Start/self-closing tags** — parsed with an `XmlReader` in `ConformanceLevel.Fragment` mode.
- **Tags `XmlReader` rejects** — caught `XmlException` falls through to a regex attribute scraper (`ParseAttributesFallback`) wrapped in a minimal `RawAttrReader` shim, so `HandleElement` works unchanged. This is what keeps bare `<d>` links working.

### Text-line accumulation

Because `GameConnection` splits chunks at every `>`, inline formatting tags (`<pushBold/>`, `<d>…</d>`) fragment a single visible line into many pieces. `AccumulateText` ([DrXmlParser.cs:152](../src/Genie.Core/DrXmlParser.cs#L152)) buffers raw fragments in `_textLineBuffer` and only emits a `TextEvent` when it sees a `\n` or an explicit `FlushTextLine()` (triggered at logical boundaries like `<prompt>`, `<pushStream>`, `</inv>`).

`EmitLine` ([DrXmlParser.cs:184](../src/Genie.Core/DrXmlParser.cs#L184)) does the final cleanup: HTML-decode, strip any embedded XML/ANSI, and **trim trailing whitespace only** — leading whitespace is significant (DR uses it for column alignment in `info`/`exp` output). Whitespace-only lines are dropped. A bare-text prompt (`>`, `H>`, `HR>`) emits a `PromptEvent` instead of a `TextEvent`.

### Link and bold spans

Clickable `<d cmd="…">` and external `<a href="…">` tags don't break the text — the inner text flows through normally. The parser bookmarks the buffer offset on the open tag and, on close, commits a [LinkSpan](../src/Genie.Core/GameEvents.cs) (`IsUrl=true` for `<a>`) attached to the resulting `TextEvent`. `<pushBold/>`/`<popBold/>` work the same way, producing `BoldSpan`s. When the `cmd` attribute is missing, the inner text doubles as the command (DR convention: `<d>BANK DEBT</d>` sends `BANK DEBT`). Both use small defensive stacks for nesting even though DR doesn't nest them in practice.

## Tag reference

Each recognised tag emits one or more `GameEvent`s. Tags in the `_settingsTags` skip-set ([DrXmlParser.cs:343](../src/Genie.Core/DrXmlParser.cs#L343)) — the initial Wrayth settings dump and UI-layout-only markup — are consumed silently.

### Vitals and resources

| Tag | Event | Notes |
| --- | --- | --- |
| `<progressBar id='health' value='80' text='…'/>` | `ProgressBarEvent(id, value, text)` | `id` ∈ health, mana, spirit, stamina, concentration, encumbrance. Dropped (logged) if `id` or `value` is missing. |
| `<resource id='…' value='N'/>` | `ResourceEvent(id, value)` | Absolute mana/spirit/stamina values. `<resource picture='0'/>` (a UI image hint) is ignored. |

### Time

| Tag | Event | Notes |
| --- | --- | --- |
| `<roundTime value='N'/>` | `RoundTimeEvent(expiresAt)` | `value` is the absolute Unix epoch when RT expires. Stored as `Combat.RoundTimeEnd`. |
| `<castTime value='N'/>` | `CastTimeEvent(expiresAt)` | Same shape, for spell prep. |
| `<prompt time='N'>…</prompt>` | `PromptEvent(serverTime)` | Open tag fires the event with the real timestamp; the element body (the visual `>`) is discarded. Also flushes any partial text line first. |

### Room and navigation

| Tag | Event | Notes |
| --- | --- | --- |
| `<streamWindow id='room' subtitle=' - [Foo, Bar]'/>` | `WindowEvent(id, title)` + synthetic `ComponentEvent("room title", "[Foo, Bar]")` | DR carries the **room title in the subtitle** of the room stream window — there is no `<component id='room title'>`. The parser bridges it so the mapper sees titles. Nested brackets pair last-`[` with last-`]`. |
| `<component id='room desc'>…</component>` | `ComponentEvent("room desc", text)` | Also: `room objs`, `room players`, `room exits`. Content accumulates between open/close. |
| `<component id='exp Climbing'>…</component>` | `ComponentEvent("exp climbing", text)` | Skill rank ticks. [GameStateEngine](../src/Genie.Core/GameStateEngine.cs) parses the rank int into `LiveSkills` for the weighted pathfinder. |
| `<compass><dir value='n'/>…</compass>` | `CompassEvent(rawXml)` | Space-joined direction tokens. Surfaced as `$roomexits` and per-exit booleans. |
| `<nav rm='12345'/>` | `NavEvent(roomId)` | Server-assigned room id — the mapper's most reliable fingerprint. |

### Hands, spell, inventory

| Tag | Event | Notes |
| --- | --- | --- |
| `<right exist='29' noun='sword'>an iron sword</right>` | `HeldItemEvent(Hand.Right, noun, exist)` | Event fires from the attributes immediately; the body display name is discarded (so `$righthand` == `$righthandnoun` for now). **Exception:** the server sometimes merges a response into the body with no separator (`<right noun='ledger'>black ledgerYou unlock and open…</right>`); the parser splits on the first lower→upper seam and re-emits the appended game text (which would otherwise be lost) on the active stream. `<left>` mirrors it. |
| `<spell>Fire Strike</spell>` | `SpellEvent(name)` | Prepared spell; content accumulates between tags. |
| `<inv id='stow'>a finely carved shortbow</inv>` | (routed to `inv` stream) | The parser treats `<inv>` as an implicit push to the `inv` stream so item lines don't leak into the main window, popping back on `</inv>`. |
| `<container id='stow' title='My Backpack' target='#37666728'/>` | `ContainerEvent(logicalId, title, targetId)` | Lets the UI render `#NNNN` ids as human names in click-echoes. Skipped if `target` is empty. |

### Streams and styling

| Tag | Event | Notes |
| --- | --- | --- |
| `<pushStream id='familiar'/>` | `StreamPushEvent(id)` | Subsequent text routes to the named stream; flushes the current line first. |
| `<popStream/>` | `StreamPopEvent(from, to)` | Returns to the previous stream. |
| `<clearStream id='…'/>` | `ClearStreamEvent(id)` | Tells the UI to wipe a stream panel. |
| `<output class='mono'/>` | `OutputClassEvent(class)` | Monospace toggle (used during EXP dumps). |
| `<preset id='roomDesc'>…</preset>` | (sets `_currentPresetId`) | Drives a `FlushTextLine` on close for `roomDesc`/`inv` so the following exits/items land on their own line. |
| `<d cmd='…'>…</d>`, `<a href='…'>…</a>` | `LinkSpan` on the next `TextEvent` | See [Link and bold spans](#link-and-bold-spans). |
| `<pushBold/>` … `<popBold/>` | `BoldSpan` on the next `TextEvent` | DR uses bold for unread news, emphasis, etc. |

### Status indicators

| Tag | Event | Notes |
| --- | --- | --- |
| `<indicator id='IconWEBBED' visible='y'/>` | `IndicatorEvent(id, visible)` | `visible` is `y`/`n`. [GameStateEngine](../src/Genie.Core/GameStateEngine.cs) maps the icon id to a `CharacterStatus`; [ScriptGlobalsSync](../src/Genie.Core/Scripting/ScriptGlobalsSync.cs) mirrors it to `$webbed`/`$standing`/etc. |

### Session lifecycle

| Tag | Event | Notes |
| --- | --- | --- |
| `<endSetup/>` | `EndSetupEvent` | End of the settings burst. |
| `<settingsInfo/>` | `SettingsInfoEvent` | Authoritative "ready for input" signal. `GenieCore` sends `look` once on this. See [SGE_PROTOCOL.md](SGE_PROTOCOL.md#after-auth). |

### Anything unrecognised

Unknown tags emit an `UnknownTagEvent(name, rawXml)` and are logged at trace level. `GameStateEngine` logs these at debug — useful when DR introduces a tag we don't yet handle, and a feed for AI-training analysis.

## Quirks the parser handles

- **Mid-tag TCP splits.** A tag cut in half across socket reads is left in `_rawBuffer` until its `>` arrives. No carry gymnastics — the incomplete tag simply isn't parsed yet.
- **`XmlReader`-hostile tags.** Bare `<d>` and friends fall through to the regex attribute scraper rather than being dropped.
- **Leading whitespace preservation.** Required for `info`/`exp` column alignment; only trailing whitespace is trimmed.
- **Inline-tag line fragmentation.** Buffered and reassembled in `_textLineBuffer`, flushed at `\n` and logical boundaries.
- **Inventory bleed.** `<inv>` content is implicitly re-routed to the `inv` stream so it doesn't appear in the main window.

## Diagnostics

- **Session Recorder** (**File → Record Session (raw XML)**) captures the verbatim raw stream to disk — the fastest way to inspect exactly what the server sent. Replay it through the engine via DevReplay mode (see [DevReplayServer](../src/Genie.Core/DevReplayServer.cs)).
- **Trace logging** on `DrXmlParser` surfaces every unknown tag.

## Code references

- **[DrXmlParser.cs](../src/Genie.Core/DrXmlParser.cs)** — the scanner, tag dispatch, span tracking, fallback attribute parser.
- **[GameEvents.cs](../src/Genie.Core/GameEvents.cs)** — the `GameEvent` record hierarchy every consumer matches on.
- **[GameConnection.cs](../src/Genie.Core/GameConnection.cs)** — socket, chunking, `RawXmlStream`, FE handshake.
- **[GameStateEngine.cs](../src/Genie.Core/GameStateEngine.cs)** — turns events into the live `GameState` snapshot.
- **[GenieCore.cs](../src/Genie.Core/GenieCore.cs)** — wires the parser to every consumer.
