using LiteDB;
using NetPilot.Core.Activity;
using NetPilot.Core.Devices;
using NetPilot.Core.Enforcement;
using NetPilot.Data.Documents;

namespace NetPilot.Data;

public class LiteActivityLogStore : IActivityLogStore, IActivityFeed
{
    private readonly NetPilotDatabase _db;
    private readonly ILiteCollection<ActivityLogDocument> _collection;
    private readonly ILiteCollection<BsonDocument> _deduplication;

    public LiteActivityLogStore(NetPilotDatabase db)
    {
        _db = db;
        _collection = db.GetCollection<ActivityLogDocument>("activity_log");
        _deduplication = db.GetCollection<BsonDocument>("activity_keys");
        _collection.EnsureIndex(a => a.AtUtc);
        _collection.EnsureIndex(a => a.Kind);
        _collection.EnsureIndex(a => a.IsNotification);
        _collection.EnsureIndex(a => a.Mac);
    }

    public Task AppendAsync(ActivityLogEntry entry, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _db.InTransaction(() =>
        {
            if (entry.DeduplicationKey is { } key && _deduplication.Exists(Query.EQ("_id", key))) return;
            var doc = ToDocument(entry);
            _collection.Insert(doc);
            if (entry.DeduplicationKey is { } addedKey)
                _deduplication.Insert(new BsonDocument { ["_id"] = addedKey, ["EventId"] = doc.Id });
        });
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ActivityLogEntry>> GetRecentAsync(int count, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ActivityLogEntry>>(_collection.Query().OrderByDescending(a => a.AtUtc)
            .Limit(count).ToEnumerable().Select(ToDomain).ToArray());

    public Task<ActivityPage> QueryAsync(ActivityFilter filter, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var query = _collection.Query();
        if (filter.BeforeId is { } before) query = query.Where(a => a.Id < before);
        if (filter.Inbox) query = query.Where(a => a.IsNotification);
        if (!string.IsNullOrWhiteSpace(filter.Mac)) query = query.Where(a => a.Mac == filter.Mac);
        // Old rows have no structured flags. Derive only from their event type, never their log text.
        if (filter.Kind != "all") query = query.Where(KindQuery(filter.Kind));
        if (filter.DryRun is { } dry) query = query.Where(dry
            ? Query.Or(Query.EQ("DryRun", true), Query.In("Type", new BsonArray { "RuleDryRun", "QuotaDryRun" }))
            : Query.And(Query.Not("DryRun", true), Query.Not("Type", "RuleDryRun"), Query.Not("Type", "QuotaDryRun")));
        if (filter.FailedOnly) query = query.Where(Query.Or(Query.EQ("Failed", true), Query.In("Type", new BsonArray { "RuleFailed", "WriteFailed" })));
        var size = Math.Clamp(filter.PageSize, 1, 100);
        var rows = query.OrderByDescending(a => a.Id).Limit(size + 1).ToEnumerable().ToArray();
        return Task.FromResult(new ActivityPage(rows.Take(size).Select(ToDomain).ToArray(), rows.Length > size ? rows[size - 1].Id : null));
    }

    private static BsonExpression KindQuery(string kind)
    {
        var types = Enum.GetNames<ActivityEventType>().Where(t => Kind(t) == kind).Select(t => new BsonValue(t));
        return Query.Or(Query.EQ("Kind", kind), Query.In("Type", new BsonArray(types)));
    }

    public Task<ActivityLogEntry?> FindAsync(int eventId, CancellationToken ct)
    {
        var doc = _collection.FindById(eventId);
        return Task.FromResult(doc is null ? null : ToDomain(doc));
    }

    public Task<int> UnreadCountAsync(CancellationToken ct) =>
        Task.FromResult(_collection.Count(a => a.IsNotification && a.ReadAtUtc == null));

    public Task MarkReadAsync(int eventId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _db.InTransaction(() =>
        {
            var doc = _collection.FindById(eventId);
            if (doc is not { IsNotification: true, ReadAtUtc: null }) return;
            doc.ReadAtUtc = DateTimeOffset.UtcNow;
            _collection.Update(doc);
        });
        return Task.CompletedTask;
    }

    public Task PruneAsync(DateTimeOffset beforeUtc, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _db.InTransaction(() =>
        {
            foreach (var doc in _collection.Find(a => a.AtUtc < beforeUtc && (!a.IsNotification || a.ReadAtUtc != null)).ToArray())
            {
                _collection.Delete(doc.Id);
                if (doc.DeduplicationKey is { } key) _deduplication.Delete(key);
            }
        });
        return Task.CompletedTask;
    }

    private static string Kind(string type) => type.StartsWith("Rule", StringComparison.Ordinal) ? "rule"
        : type.StartsWith("Quota", StringComparison.Ordinal) ? "quota"
        : type == "NotificationCreated" ? "notification" : "system";

    private static ActivityLogDocument ToDocument(ActivityLogEntry entry) => new()
    {
        AtUtc = entry.AtUtc, Type = entry.Type.ToString(), Mac = entry.Mac?.ToString(),
        Message = ActivityPrivacy.Redact(entry.Message), Source = entry.Source, Actor = entry.Actor,
        Kind = entry.Kind == "system" ? Kind(entry.Type.ToString()) : entry.Kind,
        CorrelationId = entry.CorrelationId, DeduplicationKey = entry.DeduplicationKey,
        DryRun = entry.DryRun, Failed = entry.Failed, IsNotification = entry.IsNotification,
        ReadAtUtc = entry.ReadAtUtc, ChangeId = entry.ChangeId
    };

    private static ActivityLogEntry ToDomain(ActivityLogDocument doc) => new(doc.AtUtc,
        Enum.TryParse<ActivityEventType>(doc.Type, out var type) ? type : ActivityEventType.Unknown,
        doc.Mac is null ? null : new MacAddress(doc.Mac), ActivityPrivacy.Redact(doc.Message))
    {
        EventId = doc.Id, Source = doc.Source ?? "legacy", Actor = doc.Actor ?? "Unknown",
        Kind = doc.Kind ?? Kind(doc.Type), CorrelationId = doc.CorrelationId,
        DryRun = doc.DryRun ?? (doc.Type is "RuleDryRun" or "QuotaDryRun" ? true : null),
        Failed = doc.Failed || doc.Type is "RuleFailed" or "WriteFailed", IsNotification = doc.IsNotification,
        ReadAtUtc = doc.ReadAtUtc, ChangeId = doc.ChangeId
    };
}
