namespace NetPilot.Abstractions;

/// <summary>
/// Connection settings for a configured router. Password is passed here already decrypted
/// (NetPilot.Data holds it encrypted at rest) — providers never see the encrypted form.
/// </summary>
public record RouterConnectionSettings(
    string Host,
    bool UseHttps,
    string Username,
    string Password)
{
    public const string DefaultUsername = "admin";
}
