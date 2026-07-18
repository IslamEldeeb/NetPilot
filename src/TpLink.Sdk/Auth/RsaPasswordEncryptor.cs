using System.Security.Cryptography;

namespace TpLink.Sdk.Auth;

/// <summary>
/// Encrypts the login password against the router's RSA key.
///
/// CONFIRMED LIVE 2026-07-17: raw/textbook RSA (no padding) was tried first and rejected by
/// the real router twice (with and without seq appended). Switched to PKCS#1 v1.5 padding,
/// password only (no seq) — this matches NetPilot_Research_Findings_and_Architecture.md §3.2's
/// documented TP-Link crypto primitive ("RSA, PKCS#1 v1.5 padding") directly, which the earlier
/// no-padding choice ignored based on a flawed inference (RSA ciphertext is always exactly the
/// modulus byte width regardless of padding scheme, so the live-captured 512-hex-char length
/// never actually distinguished padded from unpadded — that reasoning was wrong).
/// seq is not appended: NetPilot_Research_Findings_and_Architecture.md §3.1 step 3 describes
/// password encryption as RSA_PKCS1v1.5(password) alone; seq only enters the legacy
/// AES-envelope signing steps (4-8) that this firmware's simplified 2-call handshake skips
/// entirely per phase1-live-findings.md.
/// </summary>
public static class RsaPasswordEncryptor
{
    public static string Encrypt(string password, long seq, RsaPublicKey key)
    {
        using var rsa = RSA.Create();
        rsa.ImportParameters(new RSAParameters
        {
            Modulus = ToUnsignedBytes(key.ModulusHex, key.ModulusByteLength),
            Exponent = ToUnsignedBytes(key.ExponentHex, (key.ExponentHex.Length + 1) / 2)
        });

        var plaintextBytes = System.Text.Encoding.ASCII.GetBytes(password);
        var ciphertext = rsa.Encrypt(plaintextBytes, RSAEncryptionPadding.Pkcs1);

        return Convert.ToHexString(ciphertext).ToLowerInvariant();
    }

    private static byte[] ToUnsignedBytes(string hex, int expectedByteLength)
    {
        var normalized = hex.Length % 2 == 0 ? hex : "0" + hex;
        var bytes = Convert.FromHexString(normalized);

        if (bytes.Length == expectedByteLength)
            return bytes;

        // RSAParameters requires the modulus/exponent as unsigned big-endian byte arrays of
        // exactly the right width, with no leading zero-padding beyond what's structurally needed.
        var trimmed = bytes.SkipWhile(b => b == 0).ToArray();
        if (trimmed.Length == 0) trimmed = [0];
        return trimmed;
    }
}
