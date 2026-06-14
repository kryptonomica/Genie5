using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;
using Genie.Core.Diagnostics;
using Genie.Core.Events;
using Genie.Core.Highlights;

namespace Genie.App.Highlighting;

/// <summary>
/// Default Wrayth-style highlight set applied to incoming game text. First-rule-
/// per-character wins on overlap. Patterns are compiled once and reused.
///
/// Future work: merge with user-defined rules from <c>HighlightEngine</c> /
/// <c>NameHighlightEngine</c> in <c>Genie.Core</c>.
/// </summary>
public static class DefaultHighlights
{
    // ── Brushes (sharable, immutable) ─────────────────────────────────────────

    private static readonly IBrush CurrencyBrush   = MakeBrush(0x7C, 0xC8, 0x7C); // green
    private static readonly IBrush DirectionBrush  = MakeBrush(0x7C, 0xC8, 0xE8); // cyan
    private static readonly IBrush PossessiveBrush = MakeBrush(0xE0, 0xC0, 0x78); // gold
    private static readonly IBrush AllCapsBrush    = MakeBrush(0xE8, 0x90, 0x60); // orange
    private static readonly IBrush RoundTimeBrush  = MakeBrush(0xE0, 0x60, 0x60); // red-orange
    private static readonly IBrush NumberBrush     = MakeBrush(0xC8, 0xC8, 0x90); // tan-cream
    private static readonly IBrush RoomTitleBrush  = MakeBrush(0x90, 0xA8, 0xF0); // light blue
    private static readonly IBrush LinkBrush       = MakeBrush(0x80, 0xC0, 0xFF); // bright blue (Wrayth convention)
    private static readonly IBrush UrlBrush        = MakeBrush(0x8C, 0xD8, 0xA8); // cool green — distinguishes external URLs from game-command links

    /// <summary>
    /// Click dispatcher for &lt;d cmd&gt; links. Set by <c>MainWindowViewModel</c>
    /// at connect time so this static tokenizer doesn't need a GenieCore
    /// reference. Null until first connect, in which case the link still
    /// renders as styled but click is a no-op.
    /// <para>
    /// Signature is <c>(cmd, displayText)</c>: <c>cmd</c> is the value the
    /// server expects (with item-exist-IDs like <c>get #49489411 in
    /// #49489410</c>), <c>displayText</c> is the friendly text the user saw
    /// on screen (<c>a tapered cutlass</c>). The handler echoes <c>displayText</c>
    /// to the Game window so the player sees a readable echo instead of the
    /// raw IDs.
    /// </para>
    /// </summary>
    public static System.Action<string, string>? OnLinkClicked;

    /// <summary>
    /// Click dispatcher for <c>&lt;a href&gt;</c> URL links. Set by
    /// <c>MainWindowViewModel</c> at connect time. Passed the URL string —
    /// handler is expected to launch the OS-default browser
    /// (<c>Process.Start(url, UseShellExecute=true)</c>). Null until first
    /// connect; in that case the URL still renders styled but click is a no-op.
    /// </summary>
    public static System.Action<string>? OnUrlClicked;

    /// <summary>
    /// Sound dispatcher for highlight SFX. Set by <c>MainWindowViewModel</c> at
    /// connect time to route to <c>GenieCore.PlaySound</c> (which applies the
    /// PlaySounds gate + SoundDir resolution). Invoked once per line that a
    /// sound-carrying highlight matches. Null until connect → no sound.
    /// </summary>
    public static System.Action<string>? OnHighlightSound;

    /// <summary>
    /// Master toggle that mirrors <c>GenieConfig.ShowLinks</c>. When false,
    /// link spans render as ordinary text (no underline, no cursor change,
    /// no click handler) — useful for users who find the underlines noisy.
    /// </summary>
    public static bool LinksEnabled = true;

    // ── Patterns (ordered: earlier rules win on overlap) ──────────────────────

    private static readonly (Regex Pattern, IBrush Brush)[] Rules =
    [
        // Bracketed room titles on their own line — e.g. [Garden Rooftop, Medical Pavilion]
        (new Regex(@"^\s*\[[^\]]+\]\s*$", Opts), RoomTitleBrush),

        // "Roundtime: 3 sec." — combat readiness signal
        (new Regex(@"\bRoundtime:\s*\d+\s*sec\.?", Opts), RoundTimeBrush),

        // Compass directions when listed as exits or in movement messages
        (new Regex(@"\b(?:north|south|east|west|northeast|northwest|southeast|southwest|up|down|out)\b",
                   Opts), DirectionBrush),

        // Currency metals + DR currency names
        (new Regex(@"\b(?:platinum|gold|silver|bronze|copper)\b(?=\s+(?:Kronars|Lirums|Dokoras|coins?)|\s+to|,|\s*$|\s*\.)",
                   Opts), CurrencyBrush),
        (new Regex(@"\b(?:Kronars|Lirums|Dokoras)\b", Opts), CurrencyBrush),

        // "Your X" — common in health, vitals, status checks
        (new Regex(@"\bYour\b", Opts), PossessiveBrush),

        // All-caps tags like BANK DEBT, GO HOLD, RUMOR — 2+ caps words in a row
        (new Regex(@"\b[A-Z]{2,}(?:\s+[A-Z]{2,})+\b", Opts), AllCapsBrush),

        // Numbers >= 2 digits (skips small inline numbers like "1 sec")
        (new Regex(@"\b\d{2,}\b", Opts), NumberBrush),
    ];

    private const RegexOptions Opts = RegexOptions.Compiled | RegexOptions.CultureInvariant;

    private static IBrush MakeBrush(byte r, byte g, byte b)
        => new SolidColorBrush(Color.FromRgb(r, g, b));

    // ── Tokenizer ─────────────────────────────────────────────────────────────

    // ── User-rule brush cache (parsed hex → IBrush) ──────────────────────────
    // User-defined rules supply colours as hex strings; parse once and reuse.
    private static readonly Dictionary<string, IBrush> UserBrushCache = new(StringComparer.OrdinalIgnoreCase);

    private static IBrush? GetUserBrush(string hex)
    {
        if (string.IsNullOrEmpty(hex)) return null;
        if (UserBrushCache.TryGetValue(hex, out var brush)) return brush;
        if (!Avalonia.Media.Color.TryParse(hex, out var c)) return null;
        brush = new SolidColorBrush(c);
        UserBrushCache[hex] = brush;
        return brush;
    }

    /// <summary>
    /// Split <paramref name="text"/> into a sequence of styled <see cref="Inline"/>s.
    /// Each character is colored by the first matching rule; runs of identical-color
    /// characters collapse into a single <see cref="Run"/>. Built-in defaults run
    /// first, then user-defined rules from <see cref="UserHighlights.Engine"/>
    /// fill in any character positions the defaults didn't claim.
    ///
    /// <paramref name="links"/> overlays clickable spans (from
    /// <c>&lt;d cmd="..."&gt;</c> markup): each span emits its own
    /// <see cref="Run"/> with underline + cursor and a pointer handler
    /// that calls <see cref="OnLinkClicked"/> on press.
    ///
    /// <paramref name="boldSpans"/> overlays bold styling (from
    /// <c>&lt;pushBold/&gt;</c> / <c>&lt;popBold/&gt;</c> markers). Bold
    /// ranges interact with color highlighting orthogonally — a colored
    /// substring inside a bold span gets both color and weight.
    /// </summary>
    public static IReadOnlyList<Inline> Tokenize(string text,
                                                 IReadOnlyList<LinkSpan>? links = null,
                                                 IReadOnlyList<BoldSpan>? boldSpans = null)
    {
        if (string.IsNullOrEmpty(text))
            return new[] { new Run(string.Empty) };

        // Build a per-char color map. Null = use the default TextBlock foreground.
        var brushes = new IBrush?[text.Length];

        // Parallel per-char bold mask. Set true for every character that
        // falls inside a BoldSpan. Bold splits runs alongside color so a
        // partly-bold colored phrase emits two runs with the same brush
        // but different FontWeight.
        var bolds = new bool[text.Length];
        if (boldSpans is { Count: > 0 })
        {
            foreach (var span in boldSpans)
            {
                if (span.Length <= 0) continue;
                var spanStart = Math.Max(0, span.Start);
                var spanEnd   = Math.Min(text.Length, span.Start + span.Length);
                for (int i = spanStart; i < spanEnd; i++) bolds[i] = true;
            }
        }

        // ── Built-in default highlights (room titles, currencies, …) ──
        foreach (var (pattern, brush) in Rules)
        {
            foreach (Match m in pattern.Matches(text))
            {
                for (int i = m.Index; i < m.Index + m.Length; i++)
                    if (brushes[i] is null) brushes[i] = brush;  // first rule wins
            }
        }

        // ── User-defined highlights from the live HighlightEngine ─────
        // Timed into the Highlights stage (no-op overhead when the overlay is
        // hidden). This is the render-path cost of user highlight rules.
        if (UserHighlights.Engine is { } engine)
        {
            void ApplyUserHighlights()
            {
                foreach (var rule in engine.Rules)
                {
                    if (!rule.IsEnabled) continue;
                    if (engine.Classes is { } classes && !classes.IsActive(rule.ClassName)) continue;

                    // A rule may carry a colour, a sound, or both — so we detect
                    // the match even with no brush (sound-only highlight).
                    var ruleBrush = GetUserBrush(rule.ForegroundColor);
                    var matched   = false;
                    foreach (var (start, length) in rule.GetMatchPositions(text))
                    {
                        matched = true;
                        if (ruleBrush is null) continue;   // no colour → match-only (for sound)
                        var end = Math.Min(start + length, text.Length);
                        for (int i = start; i < end; i++)
                            if (brushes[i] is null) brushes[i] = ruleBrush;
                    }
                    // Optional per-highlight SFX, once per matching line.
                    if (matched && !string.IsNullOrEmpty(rule.SoundFile))
                        OnHighlightSound?.Invoke(rule.SoundFile);
                }
            }

            if (UserHighlights.Metrics is { } metrics)
                metrics.Time(PipelineStage.Highlights, ApplyUserHighlights);
            else
                ApplyUserHighlights();
        }

        // Order link spans by start position so we can emit non-link runs
        // between them in a single forward pass. Filter out invalid spans
        // (out-of-bounds, zero-length) defensively — the parser shouldn't
        // emit any, but better than crashing the renderer.
        List<LinkSpan>? sortedLinks = null;
        if (LinksEnabled && links is { Count: > 0 })
        {
            sortedLinks = new List<LinkSpan>(links.Count);
            foreach (var span in links)
            {
                if (span.Length <= 0) continue;
                if (span.Start < 0 || span.Start >= text.Length) continue;
                if (span.Start + span.Length > text.Length) continue;
                sortedLinks.Add(span);
            }
            sortedLinks.Sort((a, b) => a.Start.CompareTo(b.Start));
        }

        var inlines = new List<Inline>();
        int cursor = 0;
        int linkIdx = 0;

        // Walk the text. At each link boundary, flush the run of styled
        // text leading up to it, then emit a single clickable Run for the
        // link itself, then continue past.
        while (cursor < text.Length)
        {
            int nextLinkStart = sortedLinks is not null && linkIdx < sortedLinks.Count
                ? sortedLinks[linkIdx].Start
                : text.Length;

            // Emit styled (non-link) runs up to nextLinkStart.
            EmitStyledRange(text, brushes, bolds, cursor, nextLinkStart, inlines);
            cursor = nextLinkStart;

            if (sortedLinks is null || linkIdx >= sortedLinks.Count) break;

            var link = sortedLinks[linkIdx++];
            var end  = link.Start + link.Length;
            // Skip overlapping/embedded links (defensive — DR doesn't nest).
            if (end <= cursor) continue;
            var linkText = text.Substring(link.Start, link.Length);
            inlines.Add(MakeLinkRun(linkText, link.Command, link.IsUrl));
            cursor = end;
        }

        return inlines.Count == 0 ? new[] { new Run(text) } : inlines;
    }

    /// <summary>
    /// Emit a styled sequence of <see cref="Run"/>s for <paramref name="text"/>
    /// from <paramref name="start"/> (inclusive) to <paramref name="end"/>
    /// (exclusive). Runs break on EITHER a brush change OR a bold flip so a
    /// partly-bold colored phrase emits two runs (same color, different
    /// weight). Brush and bold combine orthogonally.
    /// </summary>
    private static void EmitStyledRange(string text, IBrush?[] brushes, bool[] bolds,
                                        int start, int end, List<Inline> inlines)
    {
        if (start >= end) return;
        int runStart = start;
        IBrush? currentBrush = brushes[start];
        bool    currentBold  = bolds[start];
        for (int i = start + 1; i < end; i++)
        {
            if (!ReferenceEquals(brushes[i], currentBrush) || bolds[i] != currentBold)
            {
                inlines.Add(MakeRun(text.AsSpan(runStart, i - runStart).ToString(), currentBrush, currentBold));
                runStart     = i;
                currentBrush = brushes[i];
                currentBold  = bolds[i];
            }
        }
        inlines.Add(MakeRun(text[runStart..end], currentBrush, currentBold));
    }

    private static Run MakeRun(string text, IBrush? brush, bool bold = false)
    {
        var run = new Run(text);
        if (brush is not null) run.Foreground = brush;
        if (bold)              run.FontWeight = FontWeight.Bold;
        return run;
    }

    /// <summary>
    /// Build a clickable inline. Avalonia's <see cref="Run"/> is a pure-text
    /// inline with no pointer events, so wrap a <see cref="TextBlock"/> in an
    /// <see cref="InlineUIContainer"/>. The TextBlock carries the underline,
    /// hand cursor, and click handler. We deliberately don't set FontFamily
    /// or FontSize — Avalonia inherits both from the visual parent (the
    /// SelectableTextBlock that hosts these inlines) so the link's text
    /// metrics align with the surrounding monospace flow.
    /// </summary>
    private static Inline MakeLinkRun(string text, string command, bool isUrl = false)
    {
        var tb = new TextBlock
        {
            Text             = text,
            Foreground       = isUrl ? UrlBrush : LinkBrush,
            TextDecorations  = TextDecorations.Underline,
            Cursor           = new Cursor(StandardCursorType.Hand),
            // Payload so a container that owns pointer input (the game window's
            // SelectableLinesControl, which suppresses this TextBlock's own press
            // handler to drive cross-line selection) can re-dispatch the click.
            // Other panels keep using the PointerPressed handler below.
            Tag              = new LinkPayload(command, text, isUrl),
        };
        // Tooltip shows the URL for external links — safety hint before the
        // user clicks something that will leave the game window and open a
        // browser. Game-command links don't need a tooltip because the
        // on-screen text already shows what'll be sent.
        if (isUrl) ToolTip.SetTip(tb, command);

        tb.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(tb).Properties.IsLeftButtonPressed) return;
            if (isUrl)
            {
                // External URL — route to OS browser handler.
                OnUrlClicked?.Invoke(command);
            }
            else
            {
                // Game-command link — pass both the cmd (server-bound) and the
                // visible display text (Game-window echo). VM handler decides
                // what to do with each.
                OnLinkClicked?.Invoke(command, text);
            }
            e.Handled = true;
        };
        return new InlineUIContainer { Child = tb };
    }
}

/// <summary>
/// Data carried on a clickable link inline (via <see cref="Avalonia.Controls.Control.Tag"/>)
/// so a control that owns pointer input can re-dispatch the link without the
/// inline's own <c>PointerPressed</c> handler firing. <paramref name="Display"/>
/// is the visible link text (passed to <see cref="DefaultHighlights.OnLinkClicked"/>
/// as the echo override); <paramref name="Command"/> is the server-bound command.
/// </summary>
public sealed record LinkPayload(string Command, string Display, bool IsUrl);
