using System.Security.Cryptography;

namespace TpLink.Sdk.Auth;

/// <summary>
/// Encrypts the login password against the router's RSA key for the
/// <c>POST /login?form=login</c> body's <c>password</c> field.
///
/// SCHEME CONFIRMED BY STATIC ANALYSIS of the router's own front-end JS bundles (2026-07-18,
/// fetched from the live AX53). The returning-user "Local Password" login component
/// (<c>LocalLogin</c>) calls <c>ea(rawPassword, encryptKey)</c> → <c>v.encrypt(pw, N, E)</c>.
/// The OAEP opt-in argument is absent (the injected <c>encryptKey</c> is the 2-element
/// <c>["N","E"]</c> array from <c>form=keys</c>), so the OAEP branch is never taken; execution
/// falls to <c>new Oi().setPublic(N,E).encrypt(pw)</c>. <c>Oi</c> is jsbn's RSAKey — its
/// <c>encrypt()</c> body was read directly (not assumed): it builds
/// <c>00 02 [random nonzero pad] 00 [UTF-8 message]</c> then does <c>m^e mod n</c>. That is
/// textbook RSAES-PKCS#1 v1.5 (type 2) padding of the RAW password (UTF-8 bytes) — no seq, no
/// pre-hash. The SHA256("admin"+pwd) hash seen elsewhere in the bundle feeds only the
/// post-login session envelope (Ze/m.init), which this firmware's simplified 2-call handshake
/// does not use.
///
/// UNRESOLVED CONTRADICTION: this scheme (PKCS1v1.5, raw password, no seq) was reportedly
/// rejected in earlier live attempts (see the login-research handoff). Static analysis
/// conclusively identifies the scheme but cannot explain the rejection; the cause is most
/// likely operational (rate-limit lockout false-negative, a harness password-string/encoding
/// defect, or a cookie binding between form=keys and form=login not preserved by the test
/// client), NOT the padding scheme. Do NOT change this away from PKCS1v1.5 without a
/// discriminating live capture (the exact errorcode/remainTime/failureCount from a PKCS1
/// attempt, or a DevTools breakpoint on Oi.prototype.encrypt showing the exact plaintext bytes).
/// </summary>
public static class RsaPasswordEncryptor
{
    public static string Encrypt(string password, RsaPublicKey key)
    {
        using var rsa = RSA.Create();
        rsa.ImportParameters(new RSAParameters
        {
            Modulus = ToUnsignedBytes(key.ModulusHex, key.ModulusByteLength),
            Exponent = ToUnsignedBytes(key.ExponentHex, (key.ExponentHex.Length + 1) / 2)
        });

        // UTF-8 to match jsbn's pkcs1pad2, which encodes each char via charCodeAt with UTF-8
        // multi-byte expansion. Identical to ASCII for ASCII-only passwords; correct for the rest.
        var plaintextBytes = System.Text.Encoding.UTF8.GetBytes(password);
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
