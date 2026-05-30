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
}
