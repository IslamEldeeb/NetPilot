using System.Security.Cryptography;
using TpLink.Sdk.Auth;

namespace TpLink.Sdk.Tests;

/// <summary>
/// Verifies PKCS#1 v1.5 password encryption round-trips correctly using a real 2048-bit
/// keypair. This only proves internal consistency (encrypt then decrypt with the same padding),
/// NOT correctness against the real router — the router's private key isn't available here.
/// The scheme itself (PKCS1 v1.5, raw UTF-8 password, no seq) was confirmed by static analysis
/// of the router's own login JS; see RsaPasswordEncryptor's XML doc for that analysis and the
/// still-unresolved contradiction with earlier live rejections.
/// </summary>
public class RsaPasswordEncryptorTests
{
    [Theory]
    [InlineData("admin123")]
    [InlineData("a-longer-test-password-1234567890")]
    public void Encrypt_RoundTrips_WithRealKeypair(string password)
    {
        using var rsa = RSA.Create(2048);
        var parameters = rsa.ExportParameters(includePrivateParameters: true);

        var key = new RsaPublicKey(
            Convert.ToHexString(parameters.Modulus!),
            Convert.ToHexString(parameters.Exponent!));

        var ciphertextHex = RsaPasswordEncryptor.Encrypt(password, key);

        // Ciphertext must be a fixed-width hex string matching the modulus byte length —
        // confirmed live: 512 hex chars for this router's 2048-bit/256-byte key.
        Assert.Equal(parameters.Modulus!.Length * 2, ciphertextHex.Length);

        using var decryptor = RSA.Create();
        decryptor.ImportParameters(parameters);
        var recoveredBytes = decryptor.Decrypt(Convert.FromHexString(ciphertextHex), RSAEncryptionPadding.Pkcs1);
        var recovered = System.Text.Encoding.UTF8.GetString(recoveredBytes);

        Assert.Equal(password, recovered);
    }
}
