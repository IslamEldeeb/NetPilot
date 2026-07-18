namespace NetPilot.Data.Documents;

public class RouterConnectionDocument
{
    /// <summary>v1: always 1 — single configured router. A second router later is a second row.</summary>
    public int Id { get; set; } = 1;

    public string ProviderId { get; set; } = "";
    public string Host { get; set; } = "";
    public bool UseHttps { get; set; }
    public string Username { get; set; } = "";
    public string EncryptedPassword { get; set; } = "";
}
