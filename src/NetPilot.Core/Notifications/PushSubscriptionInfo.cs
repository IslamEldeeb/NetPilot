using System.Security.Cryptography;
using System.Text;

namespace NetPilot.Core.Notifications;

public record PushSubscriptionInfo(string Id, string Endpoint, string P256dh, string Auth, string? Label,
    DateTimeOffset CreatedAtUtc, DateTimeOffset? LastSuccessAtUtc = null, int FailureCount = 0)
{
    public static string IdForEndpoint(string endpoint) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(endpoint)));
}

public interface IPushSubscriptionStore
{
    Task<IReadOnlyList<PushSubscriptionInfo>> GetAllAsync(CancellationToken ct);
    Task UpsertAsync(PushSubscriptionInfo subscription, CancellationToken ct);
    Task RemoveAsync(string id, CancellationToken ct);
    Task RecordResultAsync(string id, bool success, CancellationToken ct);
}

// Do not log or serialize this server-only key material.
public sealed class VapidKeys(string publicKey, string privateKey)
{
    public string PublicKey { get; } = publicKey;
    public string PrivateKey { get; } = privateKey;
}

public interface IVapidKeyStore
{
    Task<VapidKeys> GetOrCreateAsync(CancellationToken ct);
}
