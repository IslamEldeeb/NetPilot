namespace NetPilot.Data.Documents;

public class VapidKeyDocument
{
    public string Id { get; set; } = "default";
    public string PublicKey { get; set; } = "";
    public string ProtectedPrivateKey { get; set; } = "";
}
