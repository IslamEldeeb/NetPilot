using LiteDB;
using NetPilot.Core.Notifications;
using NetPilot.Data.Documents;

namespace NetPilot.Data;

public class LiteNotificationLogStore : INotificationLogStore
{
    private readonly ILiteCollection<NotificationLogDocument> _collection;
    private readonly NetPilotDatabase _db;

    public LiteNotificationLogStore(NetPilotDatabase db)
    {
        _db = db;
        _collection = db.GetCollection<NotificationLogDocument>("notification_log");
        _collection.EnsureIndex(d => d.AtUtc);
    }

    public Task AppendAsync(NotificationLogEntry entry, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _collection.Insert(new NotificationLogDocument
        {
            AtUtc = entry.AtUtc, Tag = entry.Tag, Title = entry.Title, ChannelId = entry.ChannelId,
            Outcome = entry.Outcome, Error = entry.Error
        });
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<NotificationLogEntry>> GetRecentAsync(int count, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<NotificationLogEntry>>(_collection.Query().OrderByDescending(d => d.AtUtc)
            .Limit(count).ToEnumerable().Select(d => new NotificationLogEntry(d.AtUtc, d.Tag, d.Title,
                d.ChannelId, d.Outcome, d.Error)).ToList());

    public Task PruneAsync(int keepNewest, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(keepNewest);
        ct.ThrowIfCancellationRequested();
        _db.InTransaction(() =>
        {
            var old = _collection.Query().OrderByDescending(d => d.AtUtc).Offset(keepNewest).ToArray();
            foreach (var doc in old) _collection.Delete(doc.Id);
        });
        return Task.CompletedTask;
    }
}
