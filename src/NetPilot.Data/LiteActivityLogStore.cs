using LiteDB;
using NetPilot.Core.Devices;
using NetPilot.Core.Enforcement;
using NetPilot.Data.Documents;

namespace NetPilot.Data;

public class LiteActivityLogStore : IActivityLogStore
{
    private readonly ILiteCollection<ActivityLogDocument> _collection;

    public LiteActivityLogStore(NetPilotDatabase db)
    {
        _collection = db.GetCollection<ActivityLogDocument>("activity_log");
        _collection.EnsureIndex(a => a.AtUtc);
    }

    public Task AppendAsync(ActivityLogEntry entry, CancellationToken ct)
    {
        _collection.Insert(new ActivityLogDocument
        {
            AtUtc = entry.AtUtc,
            Type = entry.Type.ToString(),
            Mac = entry.Mac is null ? null : (string)entry.Mac.Value,
            Message = entry.Message
        });
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ActivityLogEntry>> GetRecentAsync(int count, CancellationToken ct)
    {
        IReadOnlyList<ActivityLogEntry> entries = _collection.Query()
            .OrderByDescending(a => a.AtUtc)
            .Limit(count)
            .ToEnumerable()
            .Select(ToDomain)
            .ToList();
        return Task.FromResult(entries);
    }

    private static ActivityLogEntry ToDomain(ActivityLogDocument doc) => new(
        doc.AtUtc,
        Enum.Parse<ActivityEventType>(doc.Type),
        doc.Mac is null ? null : new MacAddress(doc.Mac),
        doc.Message);
}
