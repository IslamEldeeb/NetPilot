using LiteDB;

namespace NetPilot.Data;

/// <summary>
/// Owns the single shared LiteDB file. One instance per process (Agent and Web each open
/// their own LiteDatabase handle against the same file on the shared volume — LiteDB
/// supports this via its file-level locking, shared mode is the default for a local path).
/// </summary>
public sealed class NetPilotDatabase : IDisposable
{
    private readonly LiteDatabase _db;

    public NetPilotDatabase(string connectionString)
    {
        _db = new LiteDatabase(connectionString);
    }

    public ILiteCollection<T> GetCollection<T>(string name) => _db.GetCollection<T>(name);

    public void Dispose() => _db.Dispose();
}
