using System.Collections.Concurrent;
using System.Reactive.Linq;
using System.Text;
using Anthropic.SDK;
using Anthropic.SDK.Constants;
using Anthropic.SDK.Messaging;
using Genie.Core.Models;
using Microsoft.Extensions.Logging;

namespace Genie.Core.AI;

/// <summary>
/// Buffers raw XML from the game stream and sends it to the configured AI
/// service for:
///
///   1. <b>Parser analysis</b> — identify unknown / poorly-parsed tags so we can
///      improve <see cref="Parser.DrXmlParser"/> and <see cref="Events.GameEvents"/>.
///
///   2. <b>Gameplay analysis</b> — summarise what is happening and surface insights
///      (combat efficiency, skill training opportunities, roleplay suggestions).
///
///   3. <b>In-character advisor</b> — given recent room/dialogue context, suggest
///      contextually appropriate emotes and responses. <b>SHIPPING-GATED.</b>
///
/// The AI pipeline runs completely independently of the parser pipeline — a slow
/// AI call never blocks the game connection or script engine.
///
/// ─── ⚠ RELEASE GATES — DO NOT SHIP WITHOUT ADDRESSING ALL OF THESE ───────────
/// Captured from the DR policy compliance review (May 24, 2026 — see
/// memory/policy_compliance_review.md). Triggered by the explicit user
/// direction to flag the AI surface before public release.
///
/// G1. <b>Default OFF.</b> AI pipeline must be feature-flagged off in any
///     build that ships to the public. Opt-in checkbox in Settings with a
///     one-page privacy disclosure explaining that game text is sent to
///     the AI vendor.
///
/// G2. <b>Strip other-player content before any external send.</b> The
///     raw XML buffer currently captures EVERYTHING — including
///     &lt;preset id="whisper"&gt;, &lt;preset id="speech"&gt;,
///     &lt;pushStream id='talk|whispers|thoughts|familiar'&gt; — i.e. other
///     players' utterances. Per Simu's ToS, public-area player content is
///     licensed to Simu, not to us. <b>Shipping it to the AI vendor without a
///     filter is the single highest-risk byte in the whole Genie 5 design
///     surface.</b> Before any non-Parser-Analysis mode is enabled in a
///     public build, <see cref="SnapshotBuffer"/> (or a wrapper around it)
///     must strip those tag/stream classes.
///
/// G3. <b>InCharacterAdvisor mode stays feature-flagged off entirely.</b>
///     Even with G2's filter, the advisor mode poses additional ToS
///     concerns around perpetual-licence content. Keep it disabled until
///     there is an explicit, written legal read on the policy clauses.
///
/// G4. <b>NEVER feed AI responses back into Commands.ProcessInput.</b>
///     The pipeline must stay <i>advisory</i> — text shown to the user, who
///     decides what to type. The moment an AI response can act on the
///     game socket, every AI turn is an "unresponsive script step"
///     and the whole pipeline becomes a bot under DR's scripting policy.
///     This is the single design boundary that must not erode under any
///     "agentic" feature creep.
///
/// G5. <b>Privacy notice in-app.</b> First-time AI enable must surface a
///     plain-language summary of what gets sent off-machine and that the
///     user is also subject to the AI vendor's data handling terms.
/// ────────────────────────────────────────────────────────────────────────────
/// </summary>
public sealed class AiContextBuffer : IDisposable
{
    private readonly ILogger<AiContextBuffer>  _log;
    private readonly AnthropicClient           _anthropic;
    private readonly AiConfig                  _cfg;
    private readonly Models.GameState          _gameState;

    // ── Rolling XML buffer (thread-safe) ────────────────────────────────────
    private readonly ConcurrentQueue<string> _rawChunks = new();
    private long _totalCharsBuffered = 0;

    // ── Analysis result stream ───────────────────────────────────────────────
    public event EventHandler<AiAnalysisResult>? AnalysisReady;

    // ── Subscriptions ────────────────────────────────────────────────────────
    private readonly IDisposable _rawSubscription;
    private readonly System.Timers.Timer? _autoAnalysisTimer;

    public AiContextBuffer(
        IObservable<string>      rawXmlStream,
        Models.GameState         gameState,
        AiConfig                 cfg,
        ILogger<AiContextBuffer> log)
    {
        _log       = log;
        _cfg       = cfg;
        _gameState = gameState;
        _anthropic = new AnthropicClient(cfg.ApiKey);

        // Subscribe to raw XML — buffer every chunk
        _rawSubscription = rawXmlStream
            .Subscribe(OnRawChunk);

        // Optionally run automatic analysis on a timer (e.g., every 60 seconds)
        if (cfg.AutoAnalysisIntervalSeconds > 0)
        {
            _autoAnalysisTimer = new System.Timers.Timer(
                cfg.AutoAnalysisIntervalSeconds * 1000);
            _autoAnalysisTimer.Elapsed += (_, _) =>
                _ = AnalyzeBufferAsync(AiAnalysisMode.GameplayInsight);
            _autoAnalysisTimer.AutoReset = true;
            _autoAnalysisTimer.Start();
        }
    }

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Manually trigger an AI analysis of the current buffer contents.
    /// </summary>
    public Task AnalyzeAsync(AiAnalysisMode mode = AiAnalysisMode.GameplayInsight,
        CancellationToken ct = default)
        => AnalyzeBufferAsync(mode, ct);

    /// <summary>
    /// Ask a free-form question with the current game context injected.
    /// e.g. "What should I do next for training?" or "Suggest an in-character response."
    /// </summary>
    public async Task<string> AskAsync(string userQuestion,
        CancellationToken ct = default)
    {
        var context = BuildContextSnapshot();
        var prompt  = BuildAskPrompt(context, userQuestion);
        return await CallAiAsync(prompt, ct);
    }

    /// <summary>
    /// Feed a batch of raw XML directly (used by tests / replay analysis).
    /// </summary>
    public void InjectRaw(string xml) => OnRawChunk(xml);

    /// <summary>
    /// Returns and clears the current buffered raw XML (for inspection/logging).
    /// </summary>
    public string DrainBuffer()
    {
        var sb = new StringBuilder();
        while (_rawChunks.TryDequeue(out var chunk))
            sb.Append(chunk);
        Interlocked.Exchange(ref _totalCharsBuffered, 0);
        return sb.ToString();
    }

    // ── Internal ─────────────────────────────────────────────────────────────

    private void OnRawChunk(string chunk)
    {
        _rawChunks.Enqueue(chunk);
        Interlocked.Add(ref _totalCharsBuffered, chunk.Length);

        // Auto-flush buffer if it grows too large to avoid memory bloat
        if (_totalCharsBuffered > _cfg.MaxBufferChars)
            TrimBuffer(_cfg.MaxBufferChars / 2);
    }

    private void TrimBuffer(long keepChars)
    {
        long removed = 0;
        while (_rawChunks.TryPeek(out var chunk) &&
               Interlocked.Read(ref _totalCharsBuffered) - removed > keepChars)
        {
            if (_rawChunks.TryDequeue(out var c))
                removed += c.Length;
        }
        Interlocked.Add(ref _totalCharsBuffered, -removed);
    }

    private async Task AnalyzeBufferAsync(AiAnalysisMode mode,
        CancellationToken ct = default)
    {
        if (_rawChunks.IsEmpty)
        {
            _log.LogDebug("AI analysis requested but buffer is empty.");
            return;
        }

        try
        {
            var rawXml   = SnapshotBuffer();
            var context  = BuildContextSnapshot();
            var prompt   = BuildAnalysisPrompt(mode, rawXml, context);
            var response = await CallAiAsync(prompt, ct);

            AnalysisReady?.Invoke(this, new AiAnalysisResult(mode, response, context));
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "AI analysis failed");
        }
    }

    private string SnapshotBuffer()
    {
        // Take a snapshot without clearing (analysis is non-destructive)
        var sb = new StringBuilder();
        foreach (var chunk in _rawChunks)
            sb.Append(chunk);
        // Limit to last N characters so we don't send huge prompts
        var full = sb.ToString();
        if (full.Length <= _cfg.MaxContextChars)
            return full;
        return "[...truncated...]\n" + full[^_cfg.MaxContextChars..];
    }

    private GameContextSnapshot BuildContextSnapshot() => new()
    {
        CharacterName  = _gameState.CharacterName,
        Guild          = _gameState.Guild.ToString(),
        Race           = _gameState.Race.ToString(),
        Circle         = _gameState.Circle,
        HealthPct      = _gameState.Vitals.Health,
        ManaPct        = _gameState.Vitals.Mana,
        SpiritPct      = _gameState.Vitals.Spirit,
        StaminaPct     = _gameState.Vitals.StaminaFatigue,
        RoomTitle      = _gameState.Room.Title,
        RoomDesc       = _gameState.Room.Description,
        RoomExits      = _gameState.Room.Exits,
        PlayersPresent = _gameState.Room.Players,
        PreparedSpell  = _gameState.Combat.PreparedSpell,
        Stance         = _gameState.Combat.Stance.ToString(),
        ActiveStatuses = [.. _gameState.ActiveStatuses.Select(s => s.ToString())],
        LeftHand       = _gameState.Inventory.LeftHand,
        RightHand      = _gameState.Inventory.RightHand,
        InRoundTime    = _gameState.Combat.InRoundTime,
        RoundTimeRemaining = _gameState.Combat.RoundTimeRemaining,
        ServerTime     = _gameState.LastPrompt
    };

    // ── Prompt builders ──────────────────────────────────────────────────────

    private static string BuildAnalysisPrompt(
        AiAnalysisMode mode, string rawXml, GameContextSnapshot ctx)
    {
        var sb = new StringBuilder();

        sb.AppendLine("You are an expert assistant analyzing the DragonRealms MUD game stream.");
        sb.AppendLine("DragonRealms (play.net/dr) uses a Simutronics XML stream protocol.");
        sb.AppendLine();

        // Inject current game state context
        sb.AppendLine("## Current game state:");
        sb.AppendLine(ctx.ToPromptString());
        sb.AppendLine();

        // Raw XML sample
        sb.AppendLine("## Raw XML stream sample (most recent activity):");
        sb.AppendLine("```xml");
        sb.AppendLine(rawXml);
        sb.AppendLine("```");
        sb.AppendLine();

        switch (mode)
        {
            case AiAnalysisMode.ParserAnalysis:
                sb.AppendLine("## Task: XML Parser Analysis");
                sb.AppendLine("Analyze the raw XML above and:");
                sb.AppendLine("1. Identify any XML tags or patterns NOT in this known list:");
                sb.AppendLine("   progressBar, component, roundTime, castTime, indicator,");
                sb.AppendLine("   left, right, spell, compass, resource, pushStream, popStream,");
                sb.AppendLine("   prompt, output, streamWindow, openWindow, clearStream");
                sb.AppendLine("2. For each unknown tag: describe its apparent purpose,");
                sb.AppendLine("   list all attributes seen, and suggest a C# record name.");
                sb.AppendLine("3. Identify any component IDs not in this list:");
                sb.AppendLine("   'room title', 'room desc', 'room exits', 'room objs',");
                sb.AppendLine("   'room players', 'room extra', 'PC Health', 'PC Mana',");
                sb.AppendLine("   'PC Stamina', 'PC Spirit', 'PC Stance', 'PC Encumbrance'");
                sb.AppendLine("4. Note any parsing edge cases or malformed sequences.");
                sb.AppendLine("5. Respond in structured JSON.");
                break;

            case AiAnalysisMode.GameplayInsight:
                sb.AppendLine("## Task: Gameplay Analysis");
                sb.AppendLine("Based on the game stream and current state:");
                sb.AppendLine("1. Summarize what the character is doing (1-2 sentences).");
                sb.AppendLine("2. Identify the most important current priority.");
                sb.AppendLine("3. Note any threats or issues (low health, bleeding, etc.).");
                sb.AppendLine("4. Suggest 2-3 specific actions the player should consider.");
                sb.AppendLine("5. Note any skill training opportunities visible.");
                break;

            case AiAnalysisMode.InCharacterAdvisor:
                // ⚠ SHIPPING-GATED — see gates G1–G3 + G5 on AiContextBuffer.
                // Until a written legal read on Simu's perpetual-licence clause
                // exists, this branch must NOT execute in a public build. The
                // mode is enumerated here so the prompt builder compiles, but
                // calling it from a non-dev path is a release-blocker bug.
                sb.AppendLine("## Task: In-Character Roleplay Advisor");
                sb.AppendLine("Based on the recent room text and player context:");
                sb.AppendLine("1. Identify any social interactions or NPC dialogue.");
                sb.AppendLine("2. Suggest 2-3 in-character responses or emotes appropriate");
                sb.AppendLine("   to the situation, written in DragonRealms style.");
                sb.AppendLine("3. Note any lore-relevant details in the current environment.");
                sb.AppendLine("4. Keep suggestions brief — single commands or short sayings.");
                break;
        }

        return sb.ToString();
    }

    private static string BuildAskPrompt(GameContextSnapshot ctx, string question)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are an expert assistant for the DragonRealms text RPG (play.net/dr).");
        sb.AppendLine();
        sb.AppendLine("## Current game state:");
        sb.AppendLine(ctx.ToPromptString());
        sb.AppendLine();
        sb.AppendLine($"## Player question: {question}");
        sb.AppendLine();
        sb.AppendLine("Answer concisely and practically. Reference specific DR mechanics where relevant.");
        return sb.ToString();
    }

    // ── AI vendor API call ───────────────────────────────────────────────────
    //
    // The two lines flagged "external API surface" below reference symbols
    // exported by the Anthropic.SDK NuGet package — external contracts we
    // don't control. Everything in this codebase that we DO own uses
    // neutral AI-vendor language.

    private async Task<string> CallAiAsync(string prompt, CancellationToken ct)
    {
        _log.LogDebug("Sending {Chars} chars to AI vendor", prompt.Length);

        var request = new MessageParameters
        {
            // Vendor model constant — external API surface.
            Model      = AnthropicModels.Claude35Sonnet,
            MaxTokens  = _cfg.MaxResponseTokens,
            Messages   =
            [
                new Message(RoleType.User, prompt)
            ]
        };

        // Vendor SDK method name — external API surface.
        var response = await _anthropic.Messages.GetClaudeMessageAsync(request, ct);

        var text = response.Content
            .OfType<TextContent>()
            .Select(b => b.Text)
            .FirstOrDefault() ?? "";

        _log.LogDebug("AI vendor response: {Len} chars", text.Length);
        return text;
    }

    public void Dispose()
    {
        _rawSubscription.Dispose();
        _autoAnalysisTimer?.Dispose();
    }
}

// ── Supporting types ─────────────────────────────────────────────────────────

public enum AiAnalysisMode
{
    /// <summary>Analyze unknown XML tags to improve the parser.</summary>
    ParserAnalysis,
    /// <summary>Summarize what's happening and suggest next actions.</summary>
    GameplayInsight,
    /// <summary>
    /// Suggest in-character roleplay responses.
    /// <para>
    /// ⚠ <b>SHIPPING-GATED</b>: do not enable in any public build without
    /// a written legal read on Simu's perpetual-licence clause for
    /// public-area player content. See gates G1–G3 in the
    /// <see cref="AiContextBuffer"/> class summary and the full review in
    /// <c>memory/policy_compliance_review.md</c>.
    /// </para>
    /// </summary>
    InCharacterAdvisor
}

public sealed record AiAnalysisResult(
    AiAnalysisMode       Mode,
    string               Response,
    GameContextSnapshot  Context);

/// <summary>Point-in-time snapshot of game state passed to AI prompts.</summary>
public sealed class GameContextSnapshot
{
    public string   CharacterName      { get; init; } = "";
    public string   Guild              { get; init; } = "";
    public string   Race               { get; init; } = "";
    public int      Circle             { get; init; }
    public int      HealthPct          { get; init; }
    public int      ManaPct            { get; init; }
    public int      SpiritPct          { get; init; }
    public int      StaminaPct         { get; init; }
    public string   RoomTitle          { get; init; } = "";
    public string   RoomDesc           { get; init; } = "";
    public string   RoomExits          { get; init; } = "";
    public string   PlayersPresent     { get; init; } = "";
    public string   PreparedSpell      { get; init; } = "";
    public string   Stance             { get; init; } = "";
    public string[] ActiveStatuses     { get; init; } = [];
    public string   LeftHand           { get; init; } = "";
    public string   RightHand          { get; init; } = "";
    public bool     InRoundTime        { get; init; }
    public double   RoundTimeRemaining { get; init; }
    public DateTimeOffset ServerTime   { get; init; }

    public string ToPromptString() =>
        $"""
        Character : {CharacterName} ({Race} {Guild}, Circle {Circle})
        Vitals    : Health={HealthPct}% Mana={ManaPct}% Spirit={SpiritPct}% Stamina={StaminaPct}%
        Location  : {RoomTitle}
        Exits     : {RoomExits}
        Present   : {(string.IsNullOrEmpty(PlayersPresent) ? "no one else" : PlayersPresent)}
        Hands     : L={LeftHand ?? "empty"} R={RightHand ?? "empty"}
        Combat    : Stance={Stance} Spell="{PreparedSpell}" RT={RoundTimeRemaining:0.0}s
        Status    : {(ActiveStatuses.Length > 0 ? string.Join(", ", ActiveStatuses) : "normal")}
        """;
}

public sealed class AiConfig
{
    public string ApiKey                   { get; init; } = "";
    public int    MaxBufferChars           { get; init; } = 100_000;  // ~100KB raw XML
    public int    MaxContextChars          { get; init; } = 8_000;    // chars sent to AI vendor
    public int    MaxResponseTokens        { get; init; } = 1_024;
    public int    AutoAnalysisIntervalSeconds { get; init; } = 0;     // 0 = manual only
}
