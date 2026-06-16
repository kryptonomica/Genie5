using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Genie.Core.Connection;

/// <summary>
/// Handles the Simutronics SGE (Game Entry) authentication protocol.
///
/// Protocol (matches Genie 4 Connection.cs). The handshake is identical over
/// plaintext (port 7900) and TLS (port 7910); only the transport differs —
/// see <see cref="ConnectionConfig.UseTls"/>. TLS is the default because on
/// 7900 the password is only XOR-obfuscated with a key the server sends in
/// the clear, leaving it recoverable by a network observer.
///   1. TCP/TLS to eaccess.play.net — 7910 (TLS, default) or 7900 (plaintext)
///   2. Client sends "K\n" → server returns 32 raw key bytes + trailing \n
///   3. Client sends "A\t{ACCOUNT}\t{raw-encrypted-password}\n"
///      Password encoding: for each byte i: ((password[i] - 32) ^ keyByte[i]) + 32
///      Result is raw bytes in the stream, NOT hex-encoded
///   4. Client sends "M\n" → server returns game list
///   5. Client sends "G\t{GAMECODE}\n" → server returns account/game info
///   6. Client sends "C\n" → server returns character list
///   7. Client sends "L\t{CHARCODE}\tSTORM\n" → server returns KEY/GAMEHOST/GAMEPORT
///   8. Caller opens new TCP to GAMEHOST:GAMEPORT and sends KEY\n
/// </summary>
public sealed class SgeAuthClient(ILogger<SgeAuthClient> logger)
{
    public sealed record SgeResult(string GameHost, int GamePort, string GameKey);
    public sealed record SgeCharacter(string Code, string Name);

    /// <summary>
    /// Authenticates through SGE steps 1–6 and returns the character list for the account.
    /// Does NOT proceed to login — use this to discover available characters.
    /// </summary>
    public async Task<List<SgeCharacter>> ListCharactersAsync(
        ConnectionConfig cfg,
        CancellationToken ct = default)
    {
        logger.LogInformation("Listing characters for {Account} on {Game}",
            cfg.AccountName, cfg.GameCode);

        using var tcp = new TcpClient();
        using var stream = await ConnectSgeAsync(cfg, tcp, ct);
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);

        await StreamWriteAsync(stream, "K\n", ct);
        var keyBuf = new byte[32];
        await ReadExactAsync(stream, keyBuf, ct);
        var nlBuf = new byte[1];
        _ = await stream.ReadAsync(nlBuf, ct);

        var prefix  = Encoding.ASCII.GetBytes($"A\t{cfg.AccountName.ToUpper()}\t");
        var encPw   = EncryptPassword(cfg.AccountPassword, keyBuf);
        var authMsg = new byte[prefix.Length + encPw.Length + 1];
        Buffer.BlockCopy(prefix, 0, authMsg, 0,             prefix.Length);
        Buffer.BlockCopy(encPw,  0, authMsg, prefix.Length, encPw.Length);
        authMsg[^1] = (byte)'\n';
        await stream.WriteAsync(authMsg, ct);
        await stream.FlushAsync(ct);

        var authResponse = await ReadLineAsync(reader, ct);
        var authParts = authResponse.Split('\t');
        if (!authResponse.StartsWith("A\t") || authParts.Length < 3 ||
            authParts[2].Equals("PASSWORD", StringComparison.OrdinalIgnoreCase) ||
            authParts[2].StartsWith("UNKNOWN", StringComparison.OrdinalIgnoreCase))
        {
            var reason = authParts.Length >= 3 ? authParts[2] : authResponse;
            throw new SgeAuthException(
                $"SGE auth failed — server replied: {reason}. Check account name and password.");
        }

        await StreamWriteAsync(stream, "M\n", ct);
        await ReadLineAsync(reader, ct); // game list — discard

        await StreamWriteAsync(stream, $"G\t{cfg.GameCode}\n", ct);
        await ReadLineAsync(reader, ct); // game select — discard

        await StreamWriteAsync(stream, "C\n", ct);
        var charList = await ReadLineAsync(reader, ct);
        logger.LogDebug("Character list: {CharList}", charList);

        return ParseCharacterList(charList);
    }

    public async Task<SgeResult> AuthenticateAsync(
        ConnectionConfig cfg,
        CancellationToken ct = default)
    {
        logger.LogInformation("Starting SGE authentication for {Account} on {Game}",
            cfg.AccountName, cfg.GameCode);

        using var tcp = new TcpClient();
        using var stream = await ConnectSgeAsync(cfg, tcp, ct);

        // StreamReader for text responses only; key is read as raw bytes below.
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);

        // ── Step 1: Request hash key ─────────────────────────────────────────
        await StreamWriteAsync(stream, "K\n", ct);

        // ── Step 2: Read exactly 32 raw key bytes (+ 1 newline the server appends)
        var keyBuf = new byte[32];
        await ReadExactAsync(stream, keyBuf, ct);
        // Consume the trailing \n the plain-TCP server appends after the key.
        var nlBuf = new byte[1];
        _ = await stream.ReadAsync(nlBuf, ct);
        logger.LogDebug("Received hash key (32 bytes)");

        // ── Step 3: Send encrypted password as raw bytes ─────────────────────
        // Format: "A\t{ACCOUNT}\t" + raw_encrypted_password_bytes + "\n"
        // The encrypted bytes are sent in-band; the StreamReader must not have
        // buffered anything yet (it hasn't been used), so mixing is safe here.
        var prefix    = Encoding.ASCII.GetBytes($"A\t{cfg.AccountName.ToUpper()}\t");
        var encPw     = EncryptPassword(cfg.AccountPassword, keyBuf);
        var authMsg   = new byte[prefix.Length + encPw.Length + 1];
        Buffer.BlockCopy(prefix, 0, authMsg, 0,             prefix.Length);
        Buffer.BlockCopy(encPw,  0, authMsg, prefix.Length, encPw.Length);
        authMsg[^1] = (byte)'\n';
        await stream.WriteAsync(authMsg, ct);
        await stream.FlushAsync(ct);

        // ── Step 4: Account validation ───────────────────────────────────────
        var authResponse = await ReadLineAsync(reader, ct);
        logger.LogDebug("Auth response: {AuthResponse}", authResponse);

        if (!authResponse.StartsWith("A\t"))
            throw new SgeAuthException(
                $"SGE auth failed — unexpected response: {authResponse}");

        var authParts = authResponse.Split('\t');
        // Success: response contains KEY somewhere; failure codes: PASSWORD, UNKNOWN, etc.
        if (authParts.Length < 3 ||
            authParts[2].Equals("PASSWORD", StringComparison.OrdinalIgnoreCase) ||
            authParts[2].StartsWith("UNKNOWN", StringComparison.OrdinalIgnoreCase))
        {
            var reason = authParts.Length >= 3 ? authParts[2] : authResponse;
            throw new SgeAuthException(
                $"SGE auth failed — server replied: {reason}. " +
                "Check account name and password.");
        }

        // ── Step 5: Game list → select game ─────────────────────────────────
        await StreamWriteAsync(stream, "M\n", ct);
        var gameList = await ReadLineAsync(reader, ct);
        logger.LogDebug("Game list: {GameList}", gameList);

        await StreamWriteAsync(stream, $"G\t{cfg.GameCode}\n", ct);
        var gameResponse = await ReadLineAsync(reader, ct);
        logger.LogDebug("Game select: {GameResponse}", gameResponse);

        // ── Step 6: Character list ───────────────────────────────────────────
        await StreamWriteAsync(stream, "C\n", ct);
        var charList = await ReadLineAsync(reader, ct);
        logger.LogDebug("Character list: {CharList}", charList);

        var characters = ParseCharacterList(charList);
        var charCode   = characters.FirstOrDefault(c =>
            string.Equals(c.Name, cfg.CharacterName, StringComparison.OrdinalIgnoreCase))?.Code;
        if (charCode is null)
        {
            var names = string.Join(", ", characters.Select(c => c.Name));
            throw new InvalidOperationException(
                $"Character '{cfg.CharacterName}' not found. " +
                $"Available: {(names.Length > 0 ? names : "(none)")}");
        }

        // ── Step 7: Select character → receive game entry token ──────────────
        // Always use STORM here — "WIZ" is deprecated on DR and returns PROBLEM 2.
        // Plain-text output is achieved by NOT sending the FE:/XML announcement
        // after the game server connection, which GameConnection handles via ClientMode.
        await StreamWriteAsync(stream, $"L\t{charCode}\tSTORM\n", ct);
        var loginResponse = await ReadLineAsync(reader, ct);
        // Log at Warning so this is always visible — needed to diagnose WIZ vs STORM differences.
        logger.LogWarning("SGE login response: {LoginResponse}", loginResponse);

        return ParseLoginResponse(loginResponse);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// SHA-256 fingerprint of Simutronics' self-signed eAccess TLS certificate
    /// (Subject/Issuer <c>O=Simutronics Corp.</c>; valid 2018→3017; live-verified
    /// 2026-05-31). The cert carries no CN/SAN and is not chained to a public
    /// root, so ordinary hostname/chain validation can never authenticate the
    /// server — we PIN this exact certificate instead. If Simutronics rotates
    /// it, re-probe port 7910, verify the new fingerprint out-of-band, and
    /// update this constant (or set <see cref="ConnectionConfig.UseTls"/> = false
    /// to use plaintext 7900 meanwhile).
    /// </summary>
    private const string SgeCertSha256Pin =
        "10B737E661987D15BC5C8245E3F8B78291D41ED8ABC76672ECB02FE78ED0218A";

    /// <summary>
    /// Opens the SGE transport on <paramref name="tcp"/>: plain TCP to
    /// <see cref="ConnectionConfig.SgePort"/> (7900), or — when
    /// <see cref="ConnectionConfig.UseTls"/> is set (default) — a TLS 1.2/1.3
    /// tunnel to <see cref="ConnectionConfig.SgeTlsPort"/> (7910). The returned
    /// <see cref="Stream"/> is what every subsequent handshake step reads and
    /// writes; the SGE byte protocol is identical over either transport.
    /// </summary>
    private async Task<Stream> ConnectSgeAsync(ConnectionConfig cfg, TcpClient tcp, CancellationToken ct)
    {
        var port = cfg.ResolvedSgePort;
        logger.LogDebug("Connecting to SGE {Host}:{Port} (TLS={Tls})", cfg.SgeHost, port, cfg.UseTls);

        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        connectCts.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            await tcp.ConnectAsync(cfg.SgeHost, port, connectCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Timed out connecting to SGE server {cfg.SgeHost}:{port} (15 s). " +
                $"Check that outbound TCP port {port} is not blocked by a firewall.");
        }

        if (!cfg.UseTls)
            return tcp.GetStream();

        // Same handshake, wrapped in TLS. Validation is pinned (see
        // ValidateSgeCertificate) because the server cert is self-signed.
        var ssl = new SslStream(tcp.GetStream(), leaveInnerStreamOpen: false, ValidateSgeCertificate);
        try
        {
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost          = cfg.SgeHost,
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ssl.Dispose();
            throw new InvalidOperationException(
                $"TLS handshake with {cfg.SgeHost}:{port} failed: {ex.Message}. " +
                "If Simutronics rotated their certificate, update the pin in " +
                "SgeAuthClient; meanwhile you can connect over plaintext by " +
                "setting ConnectionConfig.UseTls = false (port 7900).", ex);
        }

        logger.LogDebug("SGE TLS established: {Protocol} / {Cipher}",
            ssl.SslProtocol, ssl.NegotiatedCipherSuite);
        return ssl;
    }

    /// <summary>
    /// Certificate-pinning validation for the eAccess TLS endpoint. Simutronics
    /// presents a self-signed certificate with no CN/SAN, so the standard
    /// <see cref="SslPolicyErrors"/> are always non-<c>None</c> and are
    /// deliberately ignored. Instead we require the certificate's SHA-256
    /// fingerprint to equal <see cref="SgeCertSha256Pin"/> — which still
    /// defeats man-in-the-middle attacks, since an attacker cannot reproduce
    /// this exact certificate without its private key.
    /// </summary>
    private bool ValidateSgeCertificate(
        object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors sslPolicyErrors)
    {
        if (certificate is null)
        {
            logger.LogError("SGE TLS: server presented no certificate — refusing connection.");
            return false;
        }

        var actual = Convert.ToHexString(SHA256.HashData(certificate.GetRawCertData()));
        if (string.Equals(actual, SgeCertSha256Pin, StringComparison.OrdinalIgnoreCase))
            return true;

        logger.LogError(
            "SGE TLS certificate pin MISMATCH (policy errors: {Errors}). Expected {Expected}, got {Actual}. " +
            "Refusing the connection. If Simutronics legitimately rotated their certificate, verify the new " +
            "fingerprint out-of-band and update SgeCertSha256Pin.",
            sslPolicyErrors, SgeCertSha256Pin, actual);
        return false;
    }

    /// <summary>
    /// Genie 4 formula: ((passwordByte - 32) XOR keyByte) + 32.
    /// Key bytes are used raw — NOT offset by 32 — matching Utility.EncryptText(byte[], string).
    /// </summary>
    private static byte[] EncryptPassword(string password, byte[] keyBuf)
    {
        var result = new byte[password.Length];
        for (int i = 0; i < password.Length; i++)
            result[i] = (byte)(((password[i] - 32) ^ keyBuf[i % keyBuf.Length]) + 32);
        return result;
    }

    private static async Task StreamWriteAsync(Stream stream, string text, CancellationToken ct)
    {
        var bytes = Encoding.ASCII.GetBytes(text);
        await stream.WriteAsync(bytes, ct);
        await stream.FlushAsync(ct);
    }

    private static async Task ReadExactAsync(Stream stream, byte[] buf, CancellationToken ct)
    {
        int total = 0;
        while (total < buf.Length)
        {
            int n = await stream.ReadAsync(buf.AsMemory(total, buf.Length - total), ct);
            if (n == 0) throw new EndOfStreamException("SGE connection closed during key read.");
            total += n;
        }
    }

    private static async Task<string> ReadLineAsync(StreamReader reader, CancellationToken ct)
    {
        var line = await reader.ReadLineAsync(ct);
        return line ?? throw new EndOfStreamException("SGE connection closed unexpectedly.");
    }

    private static List<SgeCharacter> ParseCharacterList(string charListLine)
    {
        // Format: C\t{count}\t{?}\t{?}\t{?}\t{CODE}\t{NAME}\t{CODE}\t{NAME}\t...
        // Character data starts at tab-field index 5, alternating code/name pairs.
        var result = new List<SgeCharacter>();
        var parts  = charListLine.Split('\t');
        for (int i = 5; i + 1 < parts.Length; i += 2)
        {
            var code = parts[i].Trim();
            var name = parts[i + 1].Trim();
            if (code.Length > 0 && name.Length > 0)
                result.Add(new SgeCharacter(code, name));
        }
        return result;
    }

    private static SgeResult ParseLoginResponse(string response)
    {
        // Error responses: L\t[optional fields]\tPROBLEM N
        // The PROBLEM field can appear at any tab position depending on the login mode.
        var parts       = response.Split('\t');
        var problemField = parts.FirstOrDefault(p =>
            p.TrimStart().StartsWith("PROBLEM", StringComparison.OrdinalIgnoreCase));
        if (problemField is not null)
        {
            var code   = problemField.Trim();
            var detail = code switch
            {
                "PROBLEM 1" => "Account cannot access this game (billing issue).",
                "PROBLEM 2" => "Login refused — character may be already logged in, or this login mode is not supported.",
                "PROBLEM 3" => "Character is currently in game.",
                "PROBLEM 4" => "Game is unavailable.",
                _           => $"Server refused login ({code})."
            };
            throw new SgeAuthException($"SGE login refused: {detail}");
        }

        // Success: L\tOK\tKEY=xxxx\tGAMEHOST=...\tGAMEPORT=...\t...
        string? key = null, host = null;
        int port = 0;

        foreach (var part in parts)
        {
            if (part.StartsWith("KEY="))           key  = part[4..];
            else if (part.StartsWith("GAMEHOST=")) host = part[9..];
            else if (part.StartsWith("GAMEPORT=")) port = int.Parse(part[9..]);
        }

        if (key is null || host is null || port == 0)
            throw new SgeAuthException(
                $"Could not parse SGE login response: {response}");

        return new SgeResult(host, port, key);
    }
}
