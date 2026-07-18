using System.Numerics;

namespace TpLink.Sdk.Auth;

/// <summary>Public key returned by <c>form=auth</c>/<c>operation=read</c> — modulus and exponent as hex strings.</summary>
public record RsaPublicKey(string ModulusHex, string ExponentHex)
{
    public BigInteger Modulus => ParseUnsignedHex(ModulusHex);
    public BigInteger Exponent => ParseUnsignedHex(ExponentHex);

    /// <summary>Byte length of the modulus — the fixed output width every ciphertext is padded to.</summary>
    public int ModulusByteLength => (ModulusHex.Length + 1) / 2;

    private static BigInteger ParseUnsignedHex(string hex)
    {
        var bytes = Convert.FromHexString(hex.Length % 2 == 0 ? hex : "0" + hex);
        return new BigInteger(bytes, isUnsigned: true, isBigEndian: true);
    }
}
