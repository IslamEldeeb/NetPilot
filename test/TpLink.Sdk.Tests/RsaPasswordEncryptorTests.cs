using System.Security.Cryptography;
using TpLink.Sdk.Auth;

namespace TpLink.Sdk.Tests;

/// <summary>
/// Verifies PKCS#1 v1.5 password encryption round-trips correctly using a real 2048-bit
/// keypair. Confirmed live 2026-07-17 against a real AX53: PKCS1 padding, password only
/// (no seq) is what the router actually accepts — raw/textbook RSA (with and without seq
/// appended) was tried first and rejected. See RsaPasswordEncryptor's XML doc for the
/// full history.
/// </summary>
public class RsaPasswordEncryptorTests
{
    [Theory]
    [InlineData("admin123", 12345L)]
    [InlineData("a-longer-test-password-1234567890", 1L)]
    public void Encrypt_RoundTrips_WithRealKeypair(string password, long seq)
    {
        using var rsa = RSA.Create(2048);
        var parameters = rsa.ExportParameters(includePrivateParameters: true);

        var key = new RsaPublicKey(
            Convert.ToHexString(parameters.Modulus!),
            Convert.ToHexString(parameters.Exponent!));

        var ciphertextHex = RsaPasswordEncryptor.Encrypt(password, seq, key);

        // Ciphertext must be a fixed-width hex string matching the modulus byte length —
        // confirmed live: 512 hex chars for this router's 2048-bit/256-byte key.
        Assert.Equal(parameters.Modulus!.Length * 2, ciphertextHex.Length);

        using var decryptor = RSA.Create();
        decryptor.ImportParameters(parameters);
        var recoveredBytes = decryptor.Decrypt(Convert.FromHexString(ciphertextHex), RSAEncryptionPadding.OaepSHA1);
        var recovered = System.Text.Encoding.ASCII.GetString(recoveredBytes);

        Assert.Equal(password, recovered);
    }
}
