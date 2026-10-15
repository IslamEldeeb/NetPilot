using LiteDB;

namespace NetPilot.Data.Documents;

public class ProtectedDeviceDocument
{
    [BsonId]
    public string Mac { get; set; } = "";
}
