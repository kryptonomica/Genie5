namespace Genie.Core.Events;

// ── Base ─────────────────────────────────────────────────────────────────────

/// <summary>Base for all events emitted by <see cref="Parser.DrXmlParser"/>.</summary>
public abstract record GameEvent;

// ── Text output ──────────────────────────────────────────────────────────────

/// <summary>Bare text line — room descriptions, combat results, dialogue, system messages.</summary>
/// <param name="Stream">e.g. "main", "logons", "thoughts", "death".</param>
/// <param name="Text">The visible line content. Leading whitespace preserved for server-side layout.</param>
/// <param name="Links">
/// Optional click-targets carried by <c>&lt;d cmd="..."&gt;</c> elements in the
/// XML. Each span is (Start, Length, Command) — Start/Length are character
/// offsets into <see cref="Text"/>, Command is what gets sent on click (the
/// <c>cmd</c> attribute when present, or the link's visible text when not).
/// Null when the line had no link markup; never empty.
/// </param>
/// <param name="BoldSpans">
/// Optional bold-text regions carried by <c>&lt;pushBold/&gt;</c> ...
/// <c>&lt;popBold/&gt;</c> markers in the XML. DR uses bold for emphasis
/// — most visibly to flag unread items in the <c>news</c> listing. Null
/// when the line had no bold markup; never empty.
/// </param>
public sealed record TextEvent(
    string Stream,
    string Text,
    IReadOnlyList<LinkSpan>? Links     = null,
    IReadOnlyList<BoldSpan>? BoldSpans = null
) : GameEvent;

/// <summary>
/// A clickable region inside a <see cref="TextEvent"/>. DR's XML wraps these
/// in <c>&lt;d cmd="..."&gt;visible text&lt;/d&gt;</c>; the parser strips the
/// tags from <see cref="TextEvent.Text"/> and records the span here so the
/// renderer can draw the visible text underlined and dispatch the command
/// when the user clicks it.
/// </summary>
/// <summary>
/// Inline clickable span within a <see cref="TextEvent"/>.
/// <para>
/// When <see cref="IsUrl"/> is false (default), this came from a
/// <c>&lt;d cmd&gt;</c> link and <see cref="Command"/> is a game command
/// that should be sent through the normal input pipeline on click.
/// </para>
/// <para>
/// When <see cref="IsUrl"/> is true, this came from an <c>&lt;a href&gt;</c>
/// hyperlink and <see cref="Command"/> is a URL that should be opened in the
/// user's OS-default browser. DR emits these in news/login resources blocks
/// (Simucoin Store, Elanthipedia, Olwydd's, Ranik Maps, etc.).
/// </para>
/// </summary>
public sealed record LinkSpan(int Start, int Length, string Command, bool IsUrl = false);

/// <summary>
/// A bold-styled region inside a <see cref="TextEvent"/>. Carried by
/// <c>&lt;pushBold/&gt;</c> ... <c>&lt;popBold/&gt;</c> markers. The
/// parser strips the tags from <see cref="TextEvent.Text"/> and records
/// the byte offsets so the renderer can apply <c>FontWeight.Bold</c>.
/// </summary>
public sealed record BoldSpan(int Start, int Length);

// ── Vitals ───────────────────────────────────────────────────────────────────

/// <summary>
/// &lt;progressBar id="health" value="87" text="87"/&gt;
/// IDs seen in DR: health, mana, spirit, stamina, encumbrance, concentration
/// </summary>
public sealed record ProgressBarEvent(
    string BarId,    // "health" | "mana" | "spirit" | "stamina" | "encumbrance" | "concentration"
    int    Value,    // 0–100 (percentage) for health/mana/spirit/stamina
    string Text      // display label (may be "87" or "87/100" etc.)
) : GameEvent;

/// <summary>
/// &lt;resource id="mana" value="412"/&gt; — absolute value bar (seen in some DR variants).
/// </summary>
public sealed record ResourceEvent(string ResourceId, int Value) : GameEvent;

/// <summary>
/// &lt;resource picture="3025"/&gt; — DR room/scene art id. DR sends this alongside
/// room output for locations that have artwork; <c>"0"</c> means "no image"
/// (clear). Core only surfaces the id — fetching <c>DR-art/{id}.jpg</c> from
/// play.net and displaying it is an App-layer concern, gated by
/// <c>showimages</c>. The Scene panel dedups and clears on <c>"0"</c>.
/// </summary>
public sealed record RoomImageEvent(string PictureId) : GameEvent;

// ── Action timing ────────────────────────────────────────────────────────────

/// <summary>&lt;roundTime value="unix_timestamp"/&gt; — when the next action will be free.</summary>
public sealed record RoundTimeEvent(DateTimeOffset ExpiresAt) : GameEvent;

/// <summary>&lt;castTime value="unix_timestamp"/&gt; — spell casting lock expiry.</summary>
public sealed record CastTimeEvent(DateTimeOffset ExpiresAt) : GameEvent;

// ── Named components ─────────────────────────────────────────────────────────

/// <summary>
/// &lt;component id='room title'&gt;…&lt;/component&gt;
///
/// Known DR component IDs:
///   room title     — current room name
///   room desc      — room description prose
///   room objs      — visible objects in room
///   room players   — other players present
///   room exits     — available exit directions
///   room extra     — extra room details
///   PC Health      — player health text string
///   PC Mana        — player mana text string
///   PC Stamina     — player stamina text string
///   PC Spirit      — player spirit text string
///   PC Stance      — offensive/defensive stance label
///   PC Encumbrance — encumbrance description
///   PC Status      — status effects (bleeding, stunned, etc.)
/// </summary>
/// <param name="BoldNames">For components that bolded names (DR bolds creatures
/// in <c>room objs</c>), the bold phrases incl. their trailing descriptor —
/// e.g. "a brown lynx that is sleeping". Null when nothing was bold. Backs the
/// monster count.</param>
public sealed record ComponentEvent(
    string ComponentId,
    string Content,
    IReadOnlyList<string>? BoldNames = null) : GameEvent;

// ── Status indicators ────────────────────────────────────────────────────────

/// <summary>
/// &lt;indicator id="IconKNEELING" visible="y"/&gt;
///
/// Known DR indicator IDs: IconKNEELING, IconPRONE, IconSITTING, IconSTANDING,
/// IconSTUNNED, IconHIDDEN, IconINVISIBLE, IconDEAD, IconWEBBED, IconBLEEDING,
/// IconPOISONED, IconDISEASED.
/// </summary>
public sealed record IndicatorEvent(string IndicatorId, bool Visible) : GameEvent;

// ── Inventory ────────────────────────────────────────────────────────────────

public enum Hand { Left, Right }

/// <summary>&lt;left noun="sword" exist="12345"/&gt; or &lt;right .../&gt;</summary>
public sealed record HeldItemEvent(Hand Hand, string Noun, string ExistId) : GameEvent;

// ── Spell ────────────────────────────────────────────────────────────────────

/// <summary>&lt;spell&gt;Fire Ball&lt;/spell&gt; — prepared or active spell name. Empty = none.</summary>
public sealed record SpellEvent(string SpellName) : GameEvent;

// ── Navigation ───────────────────────────────────────────────────────────────

/// <summary>Raw compass tag content — exits available from current room.</summary>
public sealed record CompassEvent(string RawXml) : GameEvent;

// ── Stream routing ───────────────────────────────────────────────────────────

public sealed record StreamPushEvent(string StreamId) : GameEvent;
public sealed record StreamPopEvent(string LeavingStreamId, string ReturningStreamId) : GameEvent;
public sealed record WindowEvent(string WindowId, string Title) : GameEvent;
public sealed record ClearStreamEvent(string StreamId) : GameEvent;

// ── Prompt ───────────────────────────────────────────────────────────────────

/// <summary>
/// Server-side prompt — marks end of a server output batch.
///
/// <para>
/// <b>ServerTime</b> is the Unix-epoch timestamp from the &lt;prompt time='…'/&gt;
/// open tag. For bare-text prompts in Wizard mode (no XML) this is
/// <see cref="DateTimeOffset.MinValue"/>.
/// </para>
///
/// <para>
/// <b>Indicator</b> is the raw prompt string the server sent — typically one of:
/// <list type="bullet">
///   <item><c>&gt;</c> — ready, no special state</item>
///   <item><c>R&gt;</c> — in roundtime</item>
///   <item><c>H&gt;</c> — hidden / stalking</item>
///   <item><c>HR&gt;</c> — hidden + roundtime</item>
///   <item><c>S&gt;</c> — stunned</item>
///   <item><c>D&gt;</c> — dead</item>
/// </list>
/// In Wizard plain-text mode this is the only authoritative source of these
/// status flags — the separate &lt;indicator&gt; / &lt;roundTime&gt; tags only
/// appear in StormFront/Wrayth XML. Genie 4 scripts read this as the
/// <c>$prompt</c> variable.
/// </para>
/// </summary>
public sealed record PromptEvent(DateTimeOffset ServerTime, string Indicator = "") : GameEvent;

// ── Output styling ───────────────────────────────────────────────────────────

public sealed record OutputClassEvent(string ClassName) : GameEvent;

// ── Session lifecycle ────────────────────────────────────────────────────────

/// <summary>&lt;endSetup/&gt; — server init block complete, gameplay stream begins.</summary>
public sealed record EndSetupEvent() : GameEvent;

/// <summary>
/// &lt;nav rm="12345"/&gt; — room navigation occurred.
/// RoomId is the server-assigned numeric room ID — the mapper's primary lookup key.
/// Empty string if the server omits the rm attribute (rare).
/// </summary>
public sealed record NavEvent(string RoomId) : GameEvent;

/// <summary>
/// Character's guild, parsed from the <c>info</c> verb output line
/// (<c>Name: … Race: … Guild: X</c>). DR doesn't push guild in a structured
/// tag, so this only fires when the player runs <c>info</c>. <see cref="Guild"/>
/// is the raw display name (e.g. "Barbarian", "Moon Mage", "Commoner").
/// </summary>
public sealed record GuildEvent(string Guild) : GameEvent;

/// <summary>
/// &lt;settingsInfo .../&gt; — server init block done and ready for commands.
/// GenieCore auto-sends "look" when this fires to populate room/vitals state.
/// </summary>
public sealed record SettingsInfoEvent() : GameEvent;

// ── Inventory containers ─────────────────────────────────────────────────────

/// <summary>
/// &lt;container id='stow' title="My Backpack" target='#37666728' location='right'/&gt;
/// DR pushes one ContainerEvent per equipped container at session start
/// (and re-emits when containers change hands).
/// <para>
/// <see cref="TargetId"/> is the <c>#NNNN</c>-form server-side item ID — the
/// same form that appears inside <c>&lt;d cmd="get #NNNN in #MMMM"&gt;</c>
/// link commands. Mapping <c>TargetId → Title</c> lets the UI translate
/// link-click echoes like <c>get a tapered cutlass in #37666728</c> into
/// the human form <c>get a tapered cutlass in My Backpack</c>.
/// </para>
/// </summary>
public sealed record ContainerEvent(string LogicalId, string Title, string TargetId) : GameEvent;

// ── Unknown ──────────────────────────────────────────────────────────────────

/// <summary>Tag the parser doesn't recognise — forwarded raw to the AI for analysis.</summary>
public sealed record UnknownTagEvent(string TagName, string RawXml) : GameEvent;
