using LiteDB;

namespace NetPilot.Data;

/// <summary>
/// Owns the single shared LiteDB file. One instance per process (Agent and Web each open
/// their own LiteDatabase handle against the same file on the shared volume). LiteDB's
/// default connection mode ("Direct") opens the file once and caches it in-process for the
/// app's lifetime — safe for a single process, but a second process's writes never become
/// visible to the first. Requires "Connection=shared" so every operation reopens/re-reads
/// the file, which is what makes cross-process visibility (Agent picking up a connection
/// saved by Web, or vice versa) actually work.
/// </summary>
public sealed class NetPilotDatabase : IDisposable
{
    private readonly LiteDatabase _db;

    public NetPilotDatabase(string dbPath)
    {
        _db = new LiteDatabase($"Filename={dbPath};Connection=shared");
    }

    public ILiteCollection<T> GetCollection<T>(string name) => _db.GetCollection<T>(name);

    public void Dispose() => _db.Dispose();
}
