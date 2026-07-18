namespace NetPilot.Core.RouterConnection;

/// <summary>
/// Persisted router connection record. EncryptedPassword is opaque ciphertext produced by
/// ASP.NET Core Data Protection — NetPilot.Core never sees, stores, or logs a plaintext
/// password. v1 is a single record; a second configured router later is one more row plus
/// a RouterId, not a schema change.
/// </summary>
public record RouterConnection(
    string ProviderId,
    string Host,
    bool UseHttps,
    string Username,
    string EncryptedPassword);
