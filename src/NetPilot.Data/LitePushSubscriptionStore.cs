using LiteDB;
using NetPilot.Core.Notifications;
using NetPilot.Core.Usage;
using NetPilot.Data.Documents;

namespace NetPilot.Data;

public class LitePushSubscriptionStore(NetPilotDatabase db, UsagePeriodClock clock) : IPushSubscriptionStore
{
    private readonly ILiteCollection<PushSubscriptionDocument> _collection = db.GetCollection<PushSubscriptionDocument>("push_subscriptions");

    public Task<IReadOnlyList<PushSubscriptionInfo>> GetAllAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<PushSubscriptionInfo>>(_collection.FindAll().Select(ToDomain).ToList());

    public Task UpsertAsync(PushSubscriptionInfo subscription, CancellationToken ct)
    {
        if (!Uri.TryCreate(subscription.Endpoint, UriKind.Absolute, out var endpoint) || endpoint.Scheme != "https")
            throw new ArgumentException("Push endpoints must be absolute HTTPS URLs.", nameof(subscription));
        ct.ThrowIfCancellationRequested();
        db.InTransaction(() =>
        {
            var id = PushSubscriptionInfo.IdForEndpoint(subscription.Endpoint);
            var existing = _collection.FindById(id);
            _collection.Upsert(new PushSubscriptionDocument
            {
                Id = id, Endpoint = subscription.Endpoint, P256dh = subscription.P256dh, Auth = subscription.Auth,
                Label = subscription.Label, CreatedAtUtc = existing?.CreatedAtUtc ?? subscription.CreatedAtUtc,
                LastSuccessAtUtc = existing is null ? subscription.LastSuccessAtUtc : existing.LastSuccessAtUtc,
                FailureCount = existing?.FailureCount ?? subscription.FailureCount
            });
        });
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _collection.Delete(id);
        return Task.CompletedTask;
    }

    public Task RecordResultAsync(string id, bool success, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        db.InTransaction(() =>
        {
            var doc = _collection.FindById(id);
            if (doc is null) return;
            if (success) { doc.LastSuccessAtUtc = clock.GetUtcNow(); doc.FailureCount = 0; }
            else if (doc.FailureCount < int.MaxValue) doc.FailureCount++;
            _collection.Update(doc);
        });
        return Task.CompletedTask;
    }

    private static PushSubscriptionInfo ToDomain(PushSubscriptionDocument doc) => new(doc.Id, doc.Endpoint,
        doc.P256dh, doc.Auth, doc.Label, doc.CreatedAtUtc, doc.LastSuccessAtUtc, doc.FailureCount);
}
