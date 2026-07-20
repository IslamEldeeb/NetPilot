using LiteDB;
using NetPilot.Core.Devices;
using NetPilot.Core.Usage;
using NetPilot.Data.Documents;

namespace NetPilot.Data;

public class LiteUsageStore : IUsageStore
{
    private readonly ILiteCollection<UsageStateDocument> _state;
    private readonly ILiteCollection<UsageHistoryDocument> _history;

    public LiteUsageStore(NetPilotDatabase db)
    {
        _state = db.GetCollection<UsageStateDocument>("usage_state");
        _history = db.GetCollection<UsageHistoryDocument>("usage_history");
    }

    public Task<DeviceUsageState?> FindStateAsync(MacAddress mac, CancellationToken ct)
    {
        var doc = _state.FindById((string)mac);
        return Task.FromResult(doc is null ? null : ToDomain(doc));
    }

    public Task<IReadOnlyList<DeviceUsageState>> GetAllStatesAsync(CancellationToken ct)
    {
        IReadOnlyList<DeviceUsageState> states = _state.FindAll().Select(ToDomain).ToList();
        return Task.FromResult(states);
    }

    public Task UpsertStateAsync(DeviceUsageState state, CancellationToken ct)
    {
        _state.Upsert(ToDocument(state));
        return Task.CompletedTask;
    }

    public Task AppendHistoryAsync(UsageHistoryEntry entry, CancellationToken ct)
    {
        _history.Upsert(new UsageHistoryDocument
        {
            Id = $"{entry.Mac}|{entry.MonthKey}",
            Mac = entry.Mac,
            MonthKey = entry.MonthKey,
            TotalBytes = entry.TotalBytes,
            FinalizedAtUtc = entry.FinalizedAtUtc
        });
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<UsageHistoryEntry>> GetHistoryAsync(MacAddress mac, int monthsBack, CancellationToken ct)
    {
        IReadOnlyList<UsageHistoryEntry> entries = _history.Find(h => h.Mac == (string)mac)
            .OrderByDescending(h => h.MonthKey)
            .Take(monthsBack)
            .Select(h => new UsageHistoryEntry(new MacAddress(h.Mac), h.MonthKey, h.TotalBytes, h.FinalizedAtUtc))
            .ToList();
        return Task.FromResult(entries);
    }

    private static DeviceUsageState ToDomain(UsageStateDocument doc) => new()
    {
        Mac = new MacAddress(doc.Mac),
        LastRawCounterBytes = doc.LastRawCounterBytes,
        LastPollAtUtc = doc.LastPollAtUtc,
        CurrentMonthKey = doc.CurrentMonthKey,
        CurrentMonthBytes = doc.CurrentMonthBytes
    };

    private static UsageStateDocument ToDocument(DeviceUsageState state) => new()
    {
        Mac = state.Mac,
        LastRawCounterBytes = state.LastRawCounterBytes,
        LastPollAtUtc = state.LastPollAtUtc,
        CurrentMonthKey = state.CurrentMonthKey,
        CurrentMonthBytes = state.CurrentMonthBytes
    };
}
