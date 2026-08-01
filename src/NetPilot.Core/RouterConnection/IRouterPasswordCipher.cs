namespace NetPilot.Core.RouterConnection;

/// <summary>
/// Seam so NetPilot.Core (e.g. RouterSessionManager) can decrypt a stored router password
/// without referencing NetPilot.Data directly — NetPilot.Data's RouterPasswordProtector
/// implements this against the shared ASP.NET Core Data Protection key ring.
/// </summary>
public interface IRouterPasswordCipher
{
    string Encrypt(string plaintextPassword);
    string Decrypt(string encryptedPassword);
}
