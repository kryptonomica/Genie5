using Genie.Core.Connection;

namespace Genie.Core.Profiles;

public sealed class ConnectionProfile
{
    public Guid   Id              { get; set; } = Guid.NewGuid();
    public string Name            { get; set; } = string.Empty;
    public bool   IsSimutronics   { get; set; } = true;
    public string GameCode        { get; set; } = "DR";
    public string CharacterName   { get; set; } = string.Empty;
    public string AccountName     { get; set; } = string.Empty;
    public string Host            { get; set; } = string.Empty;
    public int    Port            { get; set; } = 4000;
    public bool   AutoConnect     { get; set; }

    /// <summary>
    /// How this profile reaches the game. <see cref="ConnectionMode.DirectSGE"/>
    /// (default) authenticates via eaccess.play.net using the account/password
    /// fields; <see cref="ConnectionMode.LichProxy"/> ignores credentials and
    /// connects straight to <see cref="Host"/>:<see cref="Port"/> where a
    /// locally-running Lich 5 proxy has already authenticated. Serialized as a
    /// number; older profiles without the field deserialize to DirectSGE (0).
    /// </summary>
    public ConnectionMode Mode    { get; set; } = ConnectionMode.DirectSGE;
    // AES-GCM encrypted, base64: nonce(12) + tag(16) + ciphertext
    public string EncryptedPassword { get; set; } = string.Empty;

    /// <summary>
    /// FE handshake identifier this profile should use. Defaults to
    /// <c>GENIE</c> (Genie 4 parity). Setting to <c>STORM</c> may cause DR
    /// to send richer click markup for usage help / news / directions.
    /// Stored per-profile so a user with multiple characters can flip
    /// independently.
    /// </summary>
    public string FrontEndId      { get; set; } = "GENIE";

    /// <summary>
    /// Name of the layout preset to auto-apply when this profile connects.
    /// Resolved against the profile's own layout store first; empty means
    /// fall back to the global default, then the built-in layout.
    /// </summary>
    public string DefaultLayoutName { get; set; } = string.Empty;

    /// <summary>
    /// Optional per-profile data root. When set, this profile's data (its
    /// Config / Scripts / Maps / Plugins / Logs / Layouts) lives under this
    /// folder instead of the default location (per-user AppData, or beside the
    /// exe in portable mode). Lets a user keep one character on a synced drive
    /// or USB stick and another local. Empty = use the default root.
    /// <para>
    /// The master profile list (<c>profiles.json</c>) and global app settings
    /// always stay in the default root — they have to, since that's where the
    /// app reads this override from in the first place.
    /// </para>
    /// </summary>
    public string DataDirectory { get; set; } = string.Empty;
}
