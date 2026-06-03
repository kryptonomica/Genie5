using System.Xml;
using System.Reactive.Subjects;
using Genie.Core.Events;
using Genie.Core.Models;
using Microsoft.Extensions.Logging;

namespace Genie.Core.Parser;

/// <summary>
/// Parses the DragonRealms XML stream into strongly-typed <see cref="GameEvent"/> objects.
///
/// DR's stream is not a well-formed XML document — it is a continuous flow of XML
/// fragments interleaved with bare text.  This parser maintains a stateful buffer
/// and recognises the following tag vocabulary (from observed Genie 4 / Lich 5 behaviour):
///
///   &lt;streamWindow/&gt;          — output window routing
///   &lt;component id='…'&gt;       — named UI component update (vitals, room, etc.)
///   &lt;roundTime value='…'/&gt;   — action roundtime expiry timestamp
///   &lt;castTime value='…'/&gt;    — spell casting roundtime
///   &lt;indicator id='…' visible='…'/&gt; — status indicators (kneeling, prone, etc.)
///   &lt;left/&gt; &lt;right/&gt;         — held items
///   &lt;spell/&gt;                 — active/prepared spell
///   &lt;compass&gt;               — available exits
///   &lt;resource id='…'/&gt;      — mana / spirit / stamina bars
///   &lt;pushStream id='…'/&gt;    — stream routing push
///   &lt;popStream/&gt;             — stream routing pop
///   &lt;prompt time='…'/&gt;      — server-side timestamp / prompt marker
///   &lt;output class='…'/&gt;     — text styling class
///   bare text lines            — room descriptions, combat text, dialogue
/// </summary>
public sealed class DrXmlParser : IDisposable
{
    private readonly ILogger<DrXmlParser> _log;

    // ── Outbound event stream ────────────────────────────────────────────────
    private readonly Subject<GameEvent> _events = new();
    public IObservable<GameEvent> GameEvents => _events;

    // ── Stream routing state ─────────────────────────────────────────────────
    private string _activeStream = "main";
    private readonly Stack<string> _streamStack = new();

    // ── Multi-chunk accumulation ─────────────────────────────────────────────
    // DR sends <component id='...'>inner text or child tags</component> and
    // <spell>Name</spell> and <compass><dir.../></compass> across separate chunks.
    private string? _pendingComponentId;
    private readonly System.Text.StringBuilder _componentBuffer = new();
    private bool _inComponent = false;
    private bool _inSpell     = false;
    private bool _inHand      = false;   // <left>…</left> / <right>…</right> body — discard
    private bool _inCompass   = false;
    private bool _inPrompt    = false;   // <prompt>…</prompt> body — route to _promptBuffer, fire on close
    private string _currentPresetId = "";
    private readonly System.Text.StringBuilder _compassBuffer = new();

    // ── Prompt indicator capture ────────────────────────────────────────────
    // The text BETWEEN <prompt time='…'> and </prompt> is the indicator chars
    // (">", "R>", "HR>", etc.). We collect it into a side buffer so it doesn't
    // pollute _textLineBuffer, and fire the PromptEvent on close — that way
    // we have both the server timestamp from the open and the indicator
    // string from the body in a single emission.
    private readonly System.Text.StringBuilder _promptBuffer = new();
    private DateTimeOffset _pendingPromptTime = DateTimeOffset.MinValue;

    // Genie 4 renders every prompt the server sends so the user can see when
    // they're in roundtime, hidden, stunned, etc. DR sends a prompt at the
    // end of EVERY server batch though, so a literal "emit on every prompt"
    // policy floods the game window with ">" lines. Compromise: emit a
    // TextEvent only when the indicator string actually changes from the
    // last one we emitted. This surfaces every state transition (> → R>,
    // R> → >, > → H>, etc.) without spamming the steady state.
    private string _lastEmittedPromptText = "";

    // ── Text line accumulation ───────────────────────────────────────────────
    // EmitChunks (GameConnection) splits at every '>' boundary, so inline
    // formatting tags like <pushBold/>, <d>text</d> break a single text line
    // into many fragments.  We accumulate raw fragments here and only emit when
    // we see a '\n' or an explicit flush call (e.g. on <prompt> or stream change).
    private readonly System.Text.StringBuilder _textLineBuffer = new();

    // ── Clickable link tracking ─────────────────────────────────────────────
    // Each open <d cmd="..."> records its position in _textLineBuffer plus
    // the command string. When </d> closes we compute the span length and
    // commit a LinkSpan to _pendingLinks. The list is consumed by EmitLine
    // and attached to the resulting TextEvent so the renderer can draw the
    // visible text as clickable.
    //
    // Nesting: DR's XML doesn't nest <d> tags in practice, but we track a
    // small stack just to stay safe — depth == 0 means we're outside any
    // link and incoming text doesn't belong to one.
    private readonly List<LinkSpan> _pendingLinks = new();
    private readonly Stack<(int Start, string? Cmd)> _linkStack = new();

    // Parallel stack for <a href='URL'> tags — DR's news/login resources
    // block uses these for external hyperlinks (Simucoin Store,
    // Elanthipedia, etc.). Kept separate from _linkStack so an interleaved
    // <a>/<d> pair (rare but possible) doesn't pop the wrong entry on
    // close; the parser uses tag name to route each close to the right
    // stack and emits the resulting LinkSpan with IsUrl=true.
    private readonly Stack<(int Start, string? Href)> _urlStack = new();

    // ── Bold tracking ──────────────────────────────────────────────────────
    // <pushBold/> marks "bold from here"; <popBold/> closes it. Both are
    // self-closing markers (NOT paired open/close), so neither has children
    // in the XML sense. We track the open position and close it on popBold
    // by recording a BoldSpan into _pendingBoldSpans, which EmitLine then
    // attaches to the TextEvent. DR doesn't nest bold in practice, but we
    // use a stack defensively for parity with the link-span design.
    private readonly List<BoldSpan> _pendingBoldSpans = new();
    private readonly Stack<int>     _boldStack        = new();

    // ── Raw buffer ───────────────────────────────────────────────────────────
    private readonly System.Text.StringBuilder _rawBuffer = new(2048);

    public DrXmlParser(ILogger<DrXmlParser> log) => _log = log;

    /// <summary>
    /// Feed one raw XML chunk (as emitted by <see cref="GameConnection"/>).
    /// </summary>
    public void Feed(string chunk)
    {
        _rawBuffer.Append(chunk);
        ProcessBuffer();
    }

    // ── Core processing ──────────────────────────────────────────────────────

    private void ProcessBuffer()
    {
        while (_rawBuffer.Length > 0)
        {
            var raw = _rawBuffer.ToString();

            // Look for the next '<' to split bare text from tags
            int tagStart = raw.IndexOf('<');

            if (tagStart < 0)
            {
                // All bare text — accumulate and clear
                AccumulateText(raw);
                _rawBuffer.Clear();
                return;
            }

            if (tagStart > 0)
            {
                // Text before the tag
                AccumulateText(raw[..tagStart]);
                _rawBuffer.Remove(0, tagStart);
                continue;
            }

            // We're at a '<' — find the matching '>'
            int tagEnd = raw.IndexOf('>');
            if (tagEnd < 0)
                return; // incomplete tag — wait for more data

            var fullTag = raw[..(tagEnd + 1)];
            _rawBuffer.Remove(0, tagEnd + 1);

            ParseTag(fullTag);
        }
    }

    // The game sends plain-text prompts like ">", "H>", "HR>" (stance + round-time).
    private static readonly System.Text.RegularExpressions.Regex _promptRe =
        new(@"^[A-Z]*>$", System.Text.RegularExpressions.RegexOptions.Compiled);

    // The `info` verb's first line: "Name: <name>   Race: <race>   Guild: <guild>".
    // Anchored to start at "Name:" and capture the trailing "Guild: X" so we
    // don't false-match on arbitrary game text mentioning a guild. Race can be
    // multi-word (e.g. "S'Kra Mur"), hence the lazy ".+?" between fields.
    private static readonly System.Text.RegularExpressions.Regex _guildRe =
        new(@"^\s*Name:\s.+?\bGuild:\s+([A-Za-z][A-Za-z' ]*?)\s*$",
            System.Text.RegularExpressions.RegexOptions.Compiled);

    // Buffers raw text fragments across inline tag splits (e.g. <pushBold/>, <d>).
    // Flushed on '\n' or explicit FlushTextLine() call.
    private void AccumulateText(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        if (_inComponent) { _componentBuffer.Append(text); return; }
        if (_inSpell)     { _componentBuffer.Append(text); return; }
        if (_inHand)      { _componentBuffer.Append(text); return; } // content discarded on </left>/</right>
        if (_inCompass)   { _compassBuffer.Append(text);   return; }
        if (_inPrompt)    { _promptBuffer.Append(text);    return; } // indicator chars, emitted on </prompt>

        _textLineBuffer.Append(text);

        // Flush every complete '\n'-terminated line.
        var buf = _textLineBuffer.ToString();
        int nlPos;
        while ((nlPos = buf.IndexOf('\n')) >= 0)
        {
            EmitLine(buf[..nlPos].TrimEnd('\r'));
            buf = buf[(nlPos + 1)..];
        }
        _textLineBuffer.Clear();
        if (buf.Length > 0) _textLineBuffer.Append(buf);
    }

    // Flush any partial line still in the buffer (called at logical boundaries like <prompt>).
    private void FlushTextLine()
    {
        if (_textLineBuffer.Length == 0) return;
        EmitLine(_textLineBuffer.ToString());
        _textLineBuffer.Clear();
    }

    // Find the index of the first lowercase→uppercase adjacency (e.g. the "Y"
    // in "ledgerYou"). DR item display names never butt an uppercase letter
    // directly against a lowercase one without a separator — a space ("Imperial
    // saber") or apostrophe ("Se'Karan") always breaks the adjacency — so this
    // boundary reliably marks where a server-merged response was concatenated
    // onto a hand body. Returns -1 when there is no such seam.
    private static int FindMergeSeam(string s)
    {
        for (var i = 1; i < s.Length; i++)
            if (char.IsLower(s[i - 1]) && char.IsUpper(s[i])) return i;
        return -1;
    }

    // Emit one complete, decoded text line as a game event.
    private void EmitLine(string rawLine)
    {
        // Decode HTML entities and strip embedded XML, then trim trailing
        // whitespace only. LEADING whitespace is significant — DR uses it for
        // visual layout (right-aligned stat labels in `info`, indented
        // inventory items, etc.). Stripping it (the old behavior) made
        // `info`/`exp` output flush-left and broke column alignment.
        //
        // IMPORTANT: link span offsets were computed against _textLineBuffer
        // BEFORE this transform, so HtmlDecode/StripBasicXml must not be
        // collapsing characters within the link spans (the parser strips
        // tags but the link inner text is already plain by the time we get
        // here). HTML entities inside link text are rare in DR; if they
        // appear we accept minor span drift rather than complicate the math.
        var stripped = System.Net.WebUtility.HtmlDecode(StripBasicXml(rawLine)).TrimEnd();

        // Snapshot and clear link + bold state regardless of whether we
        // emit — a line that's all whitespace shouldn't leak spans into
        // the next one.
        IReadOnlyList<LinkSpan>? links = _pendingLinks.Count > 0
            ? _pendingLinks.ToArray()
            : null;
        _pendingLinks.Clear();

        IReadOnlyList<BoldSpan>? boldSpans = _pendingBoldSpans.Count > 0
            ? _pendingBoldSpans.ToArray()
            : null;
        _pendingBoldSpans.Clear();

        if (stripped.Length == 0) return;

        // Bare-text prompt line (">", "H>", "HR>"). Trim leading whitespace
        // for prompt detection only — the regex anchors on '^' so accidental
        // leading spaces from XML tag splits would miss the match.
        var promptCandidate = stripped.TrimStart();
        if (_promptRe.IsMatch(promptCandidate))
        {
            // Wizard plain-text mode: this line IS the indicator. StormFront
            // mode prompts arrive as <prompt>…</prompt> XML and never reach here.
            _events.OnNext(new PromptEvent(DateTimeOffset.MinValue, promptCandidate));

            // Surface in the game window on state change only — same policy
            // as the XML branch. In WIZ mode the bare-text line was previously
            // suppressed entirely (early return before the TextEvent below);
            // with state-change gating we now show transitions to the user
            // without flooding on every steady-state ">".
            if (promptCandidate != _lastEmittedPromptText)
            {
                _events.OnNext(new TextEvent(_activeStream, promptCandidate, null, null));
                _lastEmittedPromptText = promptCandidate;
            }
            return;
        }

        // Guild detection from the `info` first line. Fire alongside the
        // TextEvent (the line still displays) so GameState + the title can
        // pick up the guild when the player runs `info`.
        var guildMatch = _guildRe.Match(stripped);
        if (guildMatch.Success)
            _events.OnNext(new GuildEvent(guildMatch.Groups[1].Value.Trim()));

        _events.OnNext(new TextEvent(_activeStream, stripped, links, boldSpans));
    }

    private void ParseTag(string tag)
    {
        // XmlReader in Fragment mode can't handle standalone end tags like </component>,
        // so extract the name directly and dispatch without a reader.
        if (tag.StartsWith("</", StringComparison.Ordinal))
        {
            var nameEnd = tag.IndexOfAny([' ', '\t', '>'], 2);
            var name    = nameEnd >= 0 ? tag[2..nameEnd] : tag[2..^1];
            HandleEndElement(name.TrimEnd('>'));
            return;
        }

        try
        {
            using var reader = XmlReader.Create(
                new System.IO.StringReader(tag),
                new XmlReaderSettings { ConformanceLevel = ConformanceLevel.Fragment });

            if (!reader.Read()) return;

            if (reader.NodeType == XmlNodeType.Element)
            {
                HandleElement(reader, tag);
                return;
            }
        }
        catch (XmlException)
        {
            // XmlReader at Fragment level rejects some perfectly-real DR tags
            // — most notably bare `<d>` (open with no attributes and no
            // self-close), which it sees as "Data at root level invalid".
            // Fall through to the manual parser below so these still
            // dispatch through HandleElement.
        }

        // Manual fallback: scrape the name and any name="value" attributes
        // out of the raw tag with a regex. Wrap in a fake XmlReader-like
        // shim (RawAttrReader below) so the existing HandleElement
        // attribute-by-name calls keep working.
        var open = tag.AsSpan();
        if (open.Length < 3 || open[0] != '<') return;
        int n = 1;
        while (n < open.Length && (char.IsLetterOrDigit(open[n]) || open[n] == '_' || open[n] == ':'))
            n++;
        if (n == 1) return;
        var elemName = new string(open[1..n]);
        var attrs    = ParseAttributesFallback(tag);
        HandleElement(new RawAttrReader(elemName, attrs), tag);
    }

    /// <summary>
    /// Pull <c>name="value"</c> and <c>name='value'</c> pairs out of a raw
    /// element tag string. Used by the fallback path when XmlReader refuses
    /// to parse a tag (e.g. bare <c>&lt;d&gt;</c>).
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex _attrRe =
        new(@"(\w+)\s*=\s*(?:""([^""]*)""|'([^']*)')",
            System.Text.RegularExpressions.RegexOptions.Compiled);

    private static Dictionary<string, string> ParseAttributesFallback(string tag)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Text.RegularExpressions.Match m in _attrRe.Matches(tag))
        {
            var name  = m.Groups[1].Value;
            var value = m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value;
            dict[name] = value;
        }
        return dict;
    }

    /// <summary>
    /// Minimal <see cref="XmlReader"/> stand-in that only implements
    /// <c>Name</c> and the attribute indexer — the two members
    /// <see cref="HandleElement"/> uses. Avoids re-flowing the element
    /// handlers when XmlReader can't parse the tag.
    /// </summary>
    private sealed class RawAttrReader : XmlReader
    {
        private readonly string _name;
        private readonly Dictionary<string, string> _attrs;
        public RawAttrReader(string name, Dictionary<string, string> attrs)
        { _name = name; _attrs = attrs; }
        public override string Name => _name;
        public override string LocalName => _name;
        public override string? GetAttribute(string name)
            => _attrs.TryGetValue(name, out var v) ? v : null;
        public override string? this[string name] => GetAttribute(name);
        // Members below are required overrides but not used by HandleElement.
        public override int AttributeCount => _attrs.Count;
        public override string BaseURI => string.Empty;
        public override int Depth => 0;
        public override bool EOF => true;
        public override bool HasValue => false;
        public override bool IsEmptyElement => false;
        public override XmlNameTable NameTable => new NameTable();
        public override string NamespaceURI => string.Empty;
        public override XmlNodeType NodeType => XmlNodeType.Element;
        public override string Prefix => string.Empty;
        public override ReadState ReadState => ReadState.Interactive;
        public override string Value => string.Empty;
        public override string GetAttribute(int i) => _attrs.Values.ElementAt(i);
        public override string? GetAttribute(string name, string? ns) => GetAttribute(name);
        public override string LookupNamespace(string prefix) => string.Empty;
        public override bool MoveToAttribute(string name) => _attrs.ContainsKey(name);
        public override bool MoveToAttribute(string name, string? ns) => _attrs.ContainsKey(name);
        public override bool MoveToElement() => true;
        public override bool MoveToFirstAttribute() => _attrs.Count > 0;
        public override bool MoveToNextAttribute() => false;
        public override bool Read() => false;
        public override bool ReadAttributeValue() => false;
        public override void ResolveEntity() { }
    }

    // Tags to silently skip — either the initial Wrayth settings dump or
    // UI-layout-only tags that carry no game-semantic information.
    private static readonly HashSet<string> _settingsTags = new(StringComparer.OrdinalIgnoreCase)
    {
        // ── Initial settings dump ────────────────────────────────────────────
        "mode", "settings", "presets", "p", "macros", "keys", "k",
        "palette", "i", "stream", "w", "cmdline", "strings", "names",
        "ignores", "vars", "scripts", "dialog", "builtin", "panels",
        "group", "toggles", "misc", "m", "app", "display", "options", "o",
        "font", "s",
        // ── Session/connection info ─────────────────────────────────────────
        "playerid",
        // ── UI dialog layout ────────────────────────────────────────────────
        "dialogdata", "opendialog", "detach", "skin", "radio", "image",
        // ── Text styling ────────────────────────────────────────────────────
        // "preset" is NOT here — its </preset> must reach HandleEndElement to
        // flush the room description before exits appear on the next line.
        // NOTE: "pushbold"/"popbold" used to be in this skip set. They're now
        // handled explicitly so each bold span emits a BoldSpan on the next
        // TextEvent (DR uses bold for unread news items, emphasis, etc.).
        "style",
        // ── Inventory container management ──────────────────────────────────
        // "inv" is NOT here — its </inv> must reach HandleEndElement to flush each item to its own line.
        // "container" is NOT here — its open tag emits a ContainerEvent so the
        // UI can build a `#NNNN → "My Backpack"` map (used by BuildLinkEcho to
        // render container-IDs as human names in click-echoes). DR's <container>
        // tags are self-closing in practice, so the close path is never hit.
        "exposecontainer", "clearcontainer",
        // ── Experience panel slot definitions ───────────────────────────────
        "compdef",
        // NOTE: "d" was previously in this skip set, which destroyed click
        // information. The tag is now handled explicitly in HandleElement /
        // HandleEndElement so each <d cmd="..."> emits a LinkSpan on the
        // resulting TextEvent.
    };

    private void HandleElement(XmlReader r, string rawTag)
    {
        var name = r.Name.ToLowerInvariant();

        if (_settingsTags.Contains(name)) return;

        switch (name)
        {
            // ── Vitals bar ───────────────────────────────────────────────
            case "progressbar":
            {
                // <progressBar id="mana" value="87" text="87" left="425" top="290" width="102" height="13"/>
                // Skip the emit if id is missing OR value attr is absent /
                // unparseable. Previously we silently emitted value=0, which
                // could briefly display "0% health" when DR sent partial bar
                // markup. Log so we can spot it without sneaking through.
                var id     = r["id"];
                var valStr = r["value"];
                if (string.IsNullOrEmpty(id) ||
                    !int.TryParse(valStr, out var v))
                {
                    _log.LogDebug("progressBar dropped: id='{Id}' value='{Val}'", id, valStr);
                    break;
                }
                var text = r["text"] ?? v.ToString();
                _events.OnNext(new ProgressBarEvent(id, v, text));
                break;
            }

            // ── Named component update ───────────────────────────────────
            case "component":
            {
                _pendingComponentId = r["id"] ?? "";
                _componentBuffer.Clear();
                _inComponent = true;
                // Content accumulates in EmitText until </component>
                break;
            }


            // ── Roundtime ────────────────────────────────────────────────
            case "roundtime":
            {
                var expiry = long.TryParse(r["value"], out var ts) ? ts : 0L;
                var expires = DateTimeOffset.FromUnixTimeSeconds(expiry);
                _events.OnNext(new RoundTimeEvent(expires));
                break;
            }

            case "casttime":
            {
                var expiry = long.TryParse(r["value"], out var ts) ? ts : 0L;
                var expires = DateTimeOffset.FromUnixTimeSeconds(expiry);
                _events.OnNext(new CastTimeEvent(expires));
                break;
            }

            // ── Status indicator ─────────────────────────────────────────
            case "indicator":
            {
                var id      = r["id"]      ?? "";
                var visible = r["visible"] == "y";
                _events.OnNext(new IndicatorEvent(id, visible));
                break;
            }

            // ── Held items ───────────────────────────────────────────────
            // Emit the event from attributes immediately; body text (display name)
            // is absorbed by _inHand and discarded — we have noun from the attribute.
            case "left":
                _events.OnNext(new HeldItemEvent(Hand.Left,  r["noun"] ?? "", r["exist"] ?? ""));
                _componentBuffer.Clear();
                _inHand = true;
                break;
            case "right":
                _events.OnNext(new HeldItemEvent(Hand.Right, r["noun"] ?? "", r["exist"] ?? ""));
                _componentBuffer.Clear();
                _inHand = true;
                break;

            // ── Prepared spell ───────────────────────────────────────────
            case "spell":
                _componentBuffer.Clear();
                _inSpell = true;
                break;

            // ── Stream routing ───────────────────────────────────────────
            case "pushstream":
            {
                FlushTextLine(); // text before a stream push belongs to the current stream
                var sid = r["id"] ?? "main";
                _streamStack.Push(_activeStream);
                _activeStream = sid;
                _events.OnNext(new StreamPushEvent(sid));
                break;
            }
            case "popstream":
            {
                FlushTextLine(); // flush stream content before popping back
                if (_streamStack.TryPop(out var prev))
                {
                    _events.OnNext(new StreamPopEvent(_activeStream, prev));
                    _activeStream = prev;
                }
                break;
            }

            // ── Prompt / timestamp ───────────────────────────────────────
            // Open tag — capture the server timestamp and start routing the
            // inner text (the indicator chars: ">", "R>", "HR>", …) into
            // _promptBuffer. We fire PromptEvent on the CLOSE tag so the
            // event carries both the timestamp and the indicator together.
            case "prompt":
            {
                FlushTextLine(); // flush any partial line before the prompt marker
                var ts = long.TryParse(r["time"], out var t) ? t : 0L;
                _pendingPromptTime = DateTimeOffset.FromUnixTimeSeconds(ts);
                _promptBuffer.Clear();
                _inPrompt = true;
                break;
            }

            // ── Compass / exits ──────────────────────────────────────────
            case "compass":
                _compassBuffer.Clear();
                _inCompass = true;
                break;

            // ── Compass direction entry ──────────────────────────────────
            case "dir" when _inCompass:
            {
                var val = r["value"] ?? "";
                if (val.Length > 0)
                {
                    if (_compassBuffer.Length > 0) _compassBuffer.Append(' ');
                    _compassBuffer.Append(val);
                }
                break;
            }

            // ── Resource bar (mana, spirit, stamina absolute values) ─────
            case "resource":
            {
                var id = r["id"] ?? "";
                if (id.Length == 0) break; // <resource picture="0"/> is a UI image hint
                var value = int.TryParse(r["value"], out var v) ? v : 0;
                _events.OnNext(new ResourceEvent(id, value));
                break;
            }

            // ── Session lifecycle ────────────────────────────────────────────
            case "endsetup":
                _events.OnNext(new EndSetupEvent());
                break;

            case "settingsinfo":
                _events.OnNext(new SettingsInfoEvent());
                break;

            // ── Clickable link ───────────────────────────────────────────────
            // <d cmd="bank debt">BANK DEBT</d> — when cmd is missing, the
            // inner text doubles as the command (e.g. <d>BANK DEBT</d>).
            // The text body flows through normal accumulation; we just
            // bookmark the start position and remember the cmd. On </d>
            // we'll know the span length.
            case "d":
            {
                // DR's protocol does not nest <d> tags in practice. If one
                // ever arrives, log it — the close handler still pops the
                // matching open, but downstream code may be confused by
                // overlapping spans. We don't refuse the nested push because
                // discarding it would unbalance the stack on the close.
                if (_linkStack.Count > 0)
                    _log.LogDebug("Nested <d> tag detected (depth={Depth}); link spans may overlap.", _linkStack.Count);
                var cmd = r["cmd"];
                _linkStack.Push((_textLineBuffer.Length, cmd));
                break;
            }

            // ── External hyperlink ─────────────────────────────────────────
            // <a href='URL'>label</a> — used in news/login resource blocks
            // to point at Simucoin Store, Elanthipedia, etc. Bookmark the
            // current buffer position + remember the href; on </a> we
            // commit a LinkSpan with IsUrl=true so the renderer's click
            // handler routes to the OS browser instead of the game socket.
            case "a":
            {
                if (_urlStack.Count > 0)
                    _log.LogDebug("Nested <a> tag detected (depth={Depth}); URL spans may overlap.", _urlStack.Count);
                var href = r["href"];
                _urlStack.Push((_textLineBuffer.Length, href));
                break;
            }

            // ── Bold styling ────────────────────────────────────────────────
            // Self-closing markers, NOT a paired open/close element. Each
            // pushBold records the current buffer position; popBold pops
            // and emits a BoldSpan into _pendingBoldSpans for EmitLine to
            // pick up. Used by DR to mark unread news items in bold and
            // to emphasize keywords elsewhere.
            case "pushbold":
                _boldStack.Push(_textLineBuffer.Length);
                break;
            case "popbold":
                if (_boldStack.TryPop(out var boldStart))
                {
                    var spanLen = _textLineBuffer.Length - boldStart;
                    if (spanLen > 0)
                        _pendingBoldSpans.Add(new BoldSpan(boldStart, spanLen));
                }
                else
                {
                    _log.LogDebug("Unmatched <popBold/> (no open bold on stack).");
                }
                break;

            // ── Inventory item line ──────────────────────────────────────────
            // Inline <inv id='stow'>...</inv> tags carry container contents
            // OUTSIDE a <pushStream id='inv'> wrapper — they arrive immediately
            // after the room burst when _activeStream has already popped back
            // to "main". To keep these items off the main game window, treat
            // the inv element as an implicit stream push/pop: flush any
            // pending main-stream text first, then switch to "inv" so the
            // body lines emit on the inventory stream. Close in HandleEndElement
            // flushes the buffered line and pops back.
            case "inv":
                FlushTextLine();
                _streamStack.Push(_activeStream);
                _activeStream = "inv";
                break;

            // ── Text styling spans ───────────────────────────────────────────
            case "preset":
                _currentPresetId = r["id"] ?? "";
                break;

            // ── Room navigation ──────────────────────────────────────────────
            case "nav":
                _events.OnNext(new NavEvent(r["rm"] ?? ""));
                break;

            // ── Inventory container declaration ──────────────────────────────
            // <container id='stow' title="My Backpack" target='#37666728' location='right'/>
            // DR emits one of these per equipped container at session start
            // and re-emits when containers move. We forward TargetId+Title so
            // the UI can substitute "#NNNN" → "My Backpack" in click-echoes.
            // Skip if target is missing or empty — without an ID there's
            // nothing to map.
            case "container":
            {
                var target = r["target"];
                if (string.IsNullOrEmpty(target)) break;
                _events.OnNext(new ContainerEvent(
                    LogicalId: r["id"]    ?? "",
                    Title:     r["title"] ?? "",
                    TargetId:  target));
                break;
            }

            // ── Output class (bold, mono, etc.) ─────────────────────────
            case "output":
                _events.OnNext(new OutputClassEvent(r["class"] ?? ""));
                break;

            // ── Window routing + room-title carrier ──────────────────────
            // DR emits the current room name as the subtitle attribute of
            //   <streamWindow id='room' title='Room' subtitle=' - [Garden Rooftop, Medical Pavilion]'/>
            // every time the player enters a new room. There is no
            // <component id='room title'> tag — that was a long-standing
            // assumption in this parser that left _state.Room.Title empty,
            // causing the mapper engine's OnStateChanged to bail on
            // string.IsNullOrWhiteSpace(title) and never fire matching or
            // RoomNotFoundInZone. Bridge the subtitle into a synthetic
            // ComponentEvent("room title", "[Title]") so GameStateEngine and
            // the mapper adapter pick it up via the existing pipeline.
            case "streamwindow":
            case "openwindow":
                // Routing hint — record but don't change active stream yet.
                _events.OnNext(new WindowEvent(r["id"] ?? "", r["title"] ?? ""));
                if (string.Equals(r["id"], "room", StringComparison.OrdinalIgnoreCase))
                {
                    // DR emits the current room name as the subtitle attribute:
                    //   subtitle=" - [Garden Rooftop, Medical Pavilion]"
                    // Rare edge case: nested brackets like " - [Garden [Inner]]"
                    // — pair the LAST '[' with the LAST ']' so we capture the
                    // innermost bracketed label (DR convention when nesting).
                    // Without this we'd grab "[Garden [Inner]" with an
                    // unbalanced count and the mapper would mis-fingerprint.
                    var subtitle = r["subtitle"] ?? "";
                    var lastOpen  = subtitle.LastIndexOf('[');
                    var lastClose = subtitle.LastIndexOf(']');
                    string title;
                    if (lastOpen >= 0 && lastClose > lastOpen)
                        title = subtitle.Substring(lastOpen, lastClose - lastOpen + 1);
                    else if (subtitle.Contains(" - "))
                        title = subtitle[(subtitle.IndexOf(" - ") + 3)..].Trim();
                    else
                        title = subtitle.TrimStart(' ', '-').Trim();
                    if (!string.IsNullOrEmpty(title))
                        _events.OnNext(new ComponentEvent("room title", title));
                }
                break;

            // ── Clear window ─────────────────────────────────────────────
            case "clearstream":
                _events.OnNext(new ClearStreamEvent(r["id"] ?? ""));
                break;

            // ── Unrecognised (log at trace for AI analysis) ──────────────
            default:
                _log.LogTrace("Unknown DR tag: {Tag}", rawTag);
                _events.OnNext(new UnknownTagEvent(name, rawTag));
                break;
        }
    }

    private void HandleEndElement(string name)
    {
        if (_settingsTags.Contains(name)) return;

        switch (name.ToLowerInvariant())
        {
            case "component" when _inComponent:
            {
                _inComponent = false;
                var content  = System.Net.WebUtility.HtmlDecode(StripBasicXml(_componentBuffer.ToString())).Trim();
                var id       = _pendingComponentId ?? "";
                _events.OnNext(new ComponentEvent(id, content));
                _pendingComponentId = null;
                _componentBuffer.Clear();
                break;
            }
            case "spell" when _inSpell:
            {
                _inSpell = false;
                var name2 = System.Net.WebUtility.HtmlDecode(StripBasicXml(_componentBuffer.ToString())).Trim();
                _events.OnNext(new SpellEvent(name2));
                _componentBuffer.Clear();
                break;
            }
            case "left" when _inHand:
            case "right" when _inHand:
            {
                _inHand = false;
                var handBody = _componentBuffer.ToString();
                _componentBuffer.Clear();
                // Normally the body is just the display name ("razor-edged
                // scimitar"), which we discard — the noun came from the
                // attribute. But the server sometimes merges a response INTO
                // the hand body with no separator:
                //   <right noun='ledger'>black ledgerYou unlock and open…</right>
                // Splitting on the first lower→upper seam recovers the
                // appended game text, which would otherwise be silently lost.
                // The prefix (the item name) is still discarded here; capturing
                // it as $righthand is a separate enhancement.
                var handSeam = FindMergeSeam(handBody);
                if (handSeam > 0) EmitLine(handBody[handSeam..]);
                break;
            }

            case "inv":
                // Force each inventory item onto its own line, then restore
                // the previous stream context (matches the implicit push in
                // HandleElement). Without the pop, subsequent main-stream
                // text would continue to emit on the "inv" stream.
                FlushTextLine();
                if (_streamStack.TryPop(out var invPrev))
                    _activeStream = invPrev;
                break;

            case "d":
                // Close a clickable link. Compute the span length from the
                // bookmark we set on <d> open, and use the inner text as the
                // command when the cmd attribute was missing (DR convention:
                // <d>BANK DEBT</d> sends "BANK DEBT" on click).
                if (_linkStack.TryPop(out var link))
                {
                    var spanLen = _textLineBuffer.Length - link.Start;
                    if (spanLen > 0)
                    {
                        var inner = _textLineBuffer.ToString(link.Start, spanLen);
                        var cmd   = string.IsNullOrEmpty(link.Cmd) ? inner : link.Cmd;
                        _pendingLinks.Add(new LinkSpan(link.Start, spanLen, cmd!));
                    }
                }
                else
                {
                    // Unmatched </d> — stack was empty. Genie 4 never observed
                    // this in practice but log it so a future protocol change
                    // doesn't silently corrupt subsequent link spans.
                    _log.LogDebug("Unmatched </d> closing tag (no open link on stack).");
                }
                break;

            case "a":
                // Close an external hyperlink. Span text is the visible label
                // (e.g. "Elanthipedia"); the href on the open tag is what we
                // pass through as Command (the URL). IsUrl=true tells the
                // renderer to route this through the OS browser path instead
                // of the game-command pipeline. If href is missing or empty,
                // skip emission — we'd have nothing useful to open. The
                // visible text stays in the buffer either way.
                if (_urlStack.TryPop(out var urlEntry))
                {
                    var spanLen = _textLineBuffer.Length - urlEntry.Start;
                    if (spanLen > 0 && !string.IsNullOrEmpty(urlEntry.Href))
                    {
                        _pendingLinks.Add(new LinkSpan(
                            urlEntry.Start, spanLen, urlEntry.Href!, IsUrl: true));
                    }
                }
                else
                {
                    _log.LogDebug("Unmatched </a> closing tag (no open URL on stack).");
                }
                break;

            case "preset":
                // Only flush for presets where the following text is a new line
                // (roomDesc → exits follow; inv → next item follows).
                // For whisper/speech the quoted content is a continuation on the
                // same line, so flushing here would split "Renucci whispers," from
                // its message.
                if (_currentPresetId is "roomDesc" or "inv")
                    FlushTextLine();
                _currentPresetId = "";
                break;

            case "prompt":
                // Close tag — emit the PromptEvent with the timestamp captured
                // at open + the indicator string accumulated from the body.
                // The raw body may contain encoded entities (e.g. "&gt;" → ">");
                // _promptBuffer holds the post-decode text since AccumulateText
                // routes already-decoded chunks here.
                if (_inPrompt)
                {
                    // HTML-decode: DR sends ">" as the entity "&gt;" inside
                    // the <prompt> body. The normal text path runs every
                    // line through HtmlDecode in EmitLine; the prompt path
                    // bypasses EmitLine (we route to _promptBuffer instead
                    // of _textLineBuffer) so we must decode here ourselves.
                    // Without this the renderer shows literal "R&gt;" / "&gt;".
                    var indicator = System.Net.WebUtility
                        .HtmlDecode(_promptBuffer.ToString()).Trim();
                    _events.OnNext(new PromptEvent(_pendingPromptTime, indicator));

                    // Surface the indicator in the game window on state change
                    // only — see _lastEmittedPromptText for rationale.
                    if (indicator.Length > 0 && indicator != _lastEmittedPromptText)
                    {
                        _events.OnNext(new TextEvent(_activeStream, indicator, null, null));
                        _lastEmittedPromptText = indicator;
                    }

                    _promptBuffer.Clear();
                    _inPrompt = false;
                }
                // Defensive: a malformed stream missing the open could leave
                // partial text in _textLineBuffer; clearing here matches the
                // pre-change behavior so a corrupt prompt doesn't leak into
                // the next line.
                _textLineBuffer.Clear();
                break;

            case "compass" when _inCompass:
            {
                _inCompass = false;
                _events.OnNext(new CompassEvent(_compassBuffer.ToString()));
                _compassBuffer.Clear();
                break;
            }
        }
    }

    // ── Utility ──────────────────────────────────────────────────────────────

    // Matches ANSI escape sequences like ESC[1m, ESC[0m, ESC[31m, etc.
    private static readonly System.Text.RegularExpressions.Regex _ansiRe =
        new(@"\x1B\[[0-9;]*[A-Za-z]", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// Strips XML formatting tags and ANSI escape codes from text,
    /// leaving only human-readable content.
    /// </summary>
    private static string StripBasicXml(string input)
    {
        if (input.Contains('\x1B'))
            input = _ansiRe.Replace(input, string.Empty);
        if (!input.Contains('<')) return input;
        return System.Text.RegularExpressions.Regex.Replace(input, "<[^>]+>", string.Empty);
    }

    public void Dispose() => _events.Dispose();
}
