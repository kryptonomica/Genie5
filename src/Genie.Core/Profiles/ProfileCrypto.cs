using System.Security.Cryptography;
using System.Text;

namespace Genie.Core.Profiles;

/// <summary>
/// Cross-platform AES-256-GCM password encryption.
/// Key is derived from a machine-stable secret using SHA-256.
/// Format: base64(nonce[12] + tag[16] + ciphertext)
/// </summary>
internal static class ProfileCrypto
{
    // Machine-stable key: mix of machine name + a fixed salt.
    // Not high-security — passwords are stored locally and this is an
    // obfuscation layer that prevents casual reading of the JSON file.
    private static readonly byte[] Key = DeriveKey();

    private static byte[] DeriveKey()
    {
        var seed = $"Genie5|{Environment.MachineName}|ProfileStore";
        return SHA256.HashData(Encoding.UTF8.GetBytes(seed));
    }

    public static string Encrypt(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return string.Empty;
        var plain = Encoding.UTF8.GetBytes(plaintext);
        var nonce = new byte[AesGcm.NonceByteSizes.MaxSize];  // 12
        var tag   = new byte[AesGcm.TagByteSizes.MaxSize];    // 16
        var cipher = new byte[plain.Length];
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(Key, AesGcm.TagByteSizes.MaxSize);
        aes.Encrypt(nonce, plain, cipher, tag);
        var blob = new byte[nonce.Length + tag.Length + cipher.Length];
        Buffer.BlockCopy(nonce, 0, blob, 0, nonce.Length);
        Buffer.BlockCopy(tag,   0, blob, nonce.Length, tag.Length);
        Buffer.BlockCopy(cipher, 0, blob, nonce.Length + tag.Length, cipher.Length);
        return Convert.ToBase64String(blob);
    }

    public static string Decrypt(string encrypted)
    {
        if (string.IsNullOrEmpty(encrypted)) return string.Empty;
        try
        {
            var blob  = Convert.FromBase64String(encrypted);
            var nonce = blob[..12];
            var tag   = blob[12..28];
            var cipher = blob[28..];
            var plain  = new byte[cipher.Length];
            using var aes = new AesGcm(Key, AesGcm.TagByteSizes.MaxSize);
            aes.Decrypt(nonce, cipher, tag, plain);
            return Encoding.UTF8.GetString(plain);
        }
        catch { return string.Empty; }
    }
}
