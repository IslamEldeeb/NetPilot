using LiteDB;
using NetPilot.Core.RouterConnection;
using NetPilot.Data.Documents;

namespace NetPilot.Data;

public class LiteRouterConnectionStore : IRouterConnectionStore
{
    private readonly ILiteCollection<RouterConnectionDocument> _collection;

    public LiteRouterConnectionStore(NetPilotDatabase db)
    {
        _collection = db.GetCollection<RouterConnectionDocument>("router_connection");
    }

    public Task<RouterConnection?> GetAsync(CancellationToken ct)
    {
        var doc = _collection.FindById(1);
        return Task.FromResult(doc is null ? null : ToDomain(doc));
    }

    public Task SaveAsync(RouterConnection connection, CancellationToken ct)
    {
        _collection.Upsert(1, ToDocument(connection));
        return Task.CompletedTask;
    }

    public Task SeedFromEnvironmentIfEmptyAsync(string providerId, string? host, string? encryptedPassword, CancellationToken ct)
    {
        if (_collection.FindById(1) is not null)
            return Task.CompletedTask;

        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(encryptedPassword))
            return Task.CompletedTask;

        _collection.Insert(1, new RouterConnectionDocument
        {
            ProviderId = providerId,
            Host = host,
            UseHttps = true,
            Username = "admin",
            EncryptedPassword = encryptedPassword
        });

        return Task.CompletedTask;
    }

    private static RouterConnection ToDomain(RouterConnectionDocument doc) =>
        new(doc.ProviderId, doc.Host, doc.UseHttps, doc.Username, doc.EncryptedPassword);

    private static RouterConnectionDocument ToDocument(RouterConnection connection) => new()
    {
        ProviderId = connection.ProviderId,
        Host = connection.Host,
        UseHttps = connection.UseHttps,
        Username = connection.Username,
        EncryptedPassword = connection.EncryptedPassword
    };
}
