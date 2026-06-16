namespace Genie.Core.Connection;

/// <summary>
/// SGE game client protocol requested during character select.
/// </summary>
public enum GameClientMode
{
    /// <summary>StormFront XML stream (default). Full structured data for parsing.</summary>
    StormFront,

    /// <summary>
    /// Wizard / plain-text mode. No XML tags — server sends human-readable text only.
    /// Used for ground-truth comparison: connect a second character in this mode alongside
    /// an XML-mode character to validate parser output against the real display text.
    /// </summary>
    Wizard
}

/// <summary>
/// How Genie.Core reaches the game server.
/// </summary>
public enum ConnectionMode
{
    /// <summary>
    /// Normal: authenticate via SGE HTTPS, then open TCP to the returned host:port.
    /// </summary>
    DirectSGE,

    /// <summary>
    /// Lich proxy: skip SGE auth entirely; connect to a local Lich-5 proxy port.
    /// Lich handles authentication and stream augmentation.
    /// </summary>
    LichProxy,

    /// <summary>
    /// Development / testing: connect to a local mock server replaying recorded XML.
    /// </summary>
    DevReplay
}

/// <summary>
/// All runtime-configurable parameters for a game connection.
/// </summary>
public sealed record ConnectionConfig
{
    // ── SGE (direct) ────────────────────────────────────────────────────────
    public string SgeHost        { get; init; } = "eaccess.play.net";
    public int    SgePort        { get; init; } = 7900;

    /// <summary>
    /// When true (default), authenticate over TLS to <see cref="SgeTlsPort"/>
    /// (7910) instead of plaintext <see cref="SgePort"/> (7900). The SGE
    /// handshake is byte-for-byte identical either way — only the transport
    /// differs. TLS is preferred because on 7900 the account password is only
    /// XOR-obfuscated with a key the server sends in the clear, so a passive
    /// network observer can recover it; TLS gives real confidentiality.
    /// Set false to fall back to plaintext 7900 (e.g. if Simutronics rotates
    /// the pinned certificate — see <c>SgeAuthClient</c>).
    /// </summary>
    public bool   UseTls         { get; init; } = true;

    /// <summary>
    /// TLS SGE port. Live-verified (2026-05-31) speaking the SGE protocol over
    /// TLS 1.2 (AES-128). Lich 5 also authenticates here by default.
    /// </summary>
    public int    SgeTlsPort     { get; init; } = 7910;

    /// <summary>
    /// The SGE port actually dialled given <see cref="UseTls"/>:
    /// 7910 for TLS, 7900 for plaintext.
    /// </summary>
    public int    ResolvedSgePort => UseTls ? SgeTlsPort : SgePort;

    public string AccountName    { get; init; } = string.Empty;
    public string AccountPassword{ get; init; } = string.Empty;
    /// <summary>
    /// The character name to select after login (DR supports multiple characters per account).
    /// </summary>
    public string CharacterName  { get; init; } = string.Empty;
    /// <summary>
    /// Game code sent to SGE — "DR" for DragonRealms Prime, "DRF" for Fallen, etc.
    /// </summary>
    public string GameCode       { get; init; } = "DR";

    // ── Lich proxy ──────────────────────────────────────────────────────────
    public string LichProxyHost  { get; init; } = "127.0.0.1";
    public int    LichProxyPort  { get; init; } = 8000;

    // ── Dev replay ──────────────────────────────────────────────────────────
    public string ReplayFilePath { get; init; } = string.Empty;
    /// <summary>
    /// Playback speed multiplier. 1.0 = real-time, 0 = as fast as possible.
    /// </summary>
    public double ReplaySpeed    { get; init; } = 1.0;

    // ── Shared ──────────────────────────────────────────────────────────────
    public ConnectionMode   Mode       { get; init; } = ConnectionMode.DirectSGE;
    /// <summary>
    /// Protocol flavour sent to SGE during character select.
    /// Use <see cref="GameClientMode.Wizard"/> for a second "ground-truth" session
    /// when running DUAL mode to validate parser output against plain text.
    /// </summary>
    public GameClientMode   ClientMode { get; init; } = GameClientMode.StormFront;
    public int    ReadTimeoutMs  { get; init; } = 30_000;
    public int    ReconnectDelayMs { get; init; } = 5_000;
    public int    MaxReconnectAttempts { get; init; } = 10;

    /// <summary>
    /// Front-end identifier sent in the post-auth FE handshake (e.g.
    /// <c>FE:GENIE /VERSION:... /XML</c>). DR appears to gate some click
    /// markup on this — clients identifying as <c>STORM</c> (the
    /// Wrayth/StormFront name) may get richer <c>&lt;d cmd&gt;</c> tags
    /// than clients identifying as <c>GENIE</c>. Defaults to <c>GENIE</c>
    /// to match Genie 4 behavior; flip to <c>STORM</c> to experiment.
    /// </summary>
    public string FrontEndId    { get; init; } = "GENIE";

    /// <summary>
    /// Optional per-profile data root. When non-empty, <see cref="GenieCore"/>
    /// points all of its data folders (Config, Scripts, Maps, Plugins, Logs,
    /// Profiles) under this path instead of the default per-user / portable
    /// location. Carries <c>ConnectionProfile.DataDirectory</c> into the core.
    /// Empty = default root.
    /// </summary>
    public string DataDirectoryOverride { get; init; } = string.Empty;
}
