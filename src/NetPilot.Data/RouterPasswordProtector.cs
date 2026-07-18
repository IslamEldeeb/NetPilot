using Microsoft.AspNetCore.DataProtection;

namespace NetPilot.Data;

/// <summary>
/// Encrypts/decrypts the router password before it touches the LiteDB file. Both
/// NetPilot.Agent and NetPilot.Web resolve this against the same shared Data Protection
/// key ring (see ServiceCollectionExtensions.AddNetPilotData) so either process can
/// decrypt what the other encrypted.
/// </summary>
public class RouterPasswordProtector
{
    private const string Purpose = "NetPilot.RouterConnection.Password";
    private readonly IDataProtector _protector;

    public RouterPasswordProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector(Purpose);
    }

    public string Encrypt(string plaintextPassword) => _protector.Protect(plaintextPassword);

    public string Decrypt(string encryptedPassword) => _protector.Unprotect(encryptedPassword);
}
