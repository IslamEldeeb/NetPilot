using LiteDB;
using NetPilot.Core.Devices;
using NetPilot.Core.Usage;
using NetPilot.Data.Documents;

namespace NetPilot.Data;

public class LiteUsageStore : IUsageStore
{
    private readonly ILiteCollection<UsageStateDocument> _state;
    private readonly ILiteCollection<UsageHistoryDocument> _history;
    private readonly ILiteCollection<UsageDailyHistoryDocument> _dailyHistory;

    public LiteUsageStore(NetPilotDatabase db)
    {
        _state = db.GetCollection<UsageStateDocument>("usage_state");
        _history = db.GetCollection<UsageHistoryDocument>("usage_history");
        _dailyHistory = db.GetCollection<UsageDailyHistoryDocument>("usage_daily_history");
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
            LanBytes = entry.LanBytes,
            FinalizedAtUtc = entry.FinalizedAtUtc
        });
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<UsageHistoryEntry>> GetAllHistoryAsync(CancellationToken ct)
    {
        IReadOnlyList<UsageHistoryEntry> entries = _history.FindAll()
            .Select(h => new UsageHistoryEntry(new MacAddress(h.Mac), h.MonthKey, h.TotalBytes, h.FinalizedAtUtc, h.LanBytes))
            .ToList();
        return Task.FromResult(entries);
    }

    public Task AppendDailyHistoryAsync(UsageDailyHistoryEntry entry, CancellationToken ct)
    {
        _dailyHistory.Upsert(new UsageDailyHistoryDocument
        {
            Id = $"{entry.Mac}|{entry.DayKey}",
            Mac = entry.Mac,
            DayKey = entry.DayKey,
            ForecastEligible = entry.ForecastEligible,
            TotalBytes = entry.TotalBytes,
            LanBytes = entry.LanBytes,
            FinalizedAtUtc = entry.FinalizedAtUtc
        });
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<UsageDailyHistoryEntry>> GetAllDailyHistoryAsync(CancellationToken ct)
    {
        IReadOnlyList<UsageDailyHistoryEntry> entries = _dailyHistory.FindAll()
            .Select(h => new UsageDailyHistoryEntry(new MacAddress(h.Mac), h.DayKey, h.TotalBytes, h.FinalizedAtUtc, h.LanBytes, h.ForecastEligible))
            .ToList();
        return Task.FromResult(entries);
    }

    private static DeviceUsageState ToDomain(UsageStateDocument doc) => new()
    {
        Mac = new MacAddress(doc.Mac),
        LastRawCounterBytes = doc.LastRawCounterBytes,
        LastPollAtUtc = doc.LastPollAtUtc,
        CurrentMonthKey = doc.CurrentMonthKey,
        CurrentMonthBytes = doc.CurrentMonthBytes,
        CurrentDayKey = doc.CurrentDayKey,
        CurrentDayLanBytes = doc.CurrentDayLanBytes,
        CurrentMonthLanBytes = doc.CurrentMonthLanBytes,
        PendingLanCarryBytes = doc.PendingLanCarryBytes,
        DayFirstPollAtUtc = doc.DayFirstPollAtUtc, DayHasSamplingGap = doc.DayHasSamplingGap,
        CurrentDayBytes = doc.CurrentDayBytes
    };

    private static UsageStateDocument ToDocument(DeviceUsageState state) => new()
    {
        Mac = state.Mac,
        LastRawCounterBytes = state.LastRawCounterBytes,
        LastPollAtUtc = state.LastPollAtUtc,
        CurrentMonthKey = state.CurrentMonthKey,
        CurrentMonthBytes = state.CurrentMonthBytes,
        CurrentDayKey = state.CurrentDayKey,
        CurrentDayLanBytes = state.CurrentDayLanBytes,
        CurrentMonthLanBytes = state.CurrentMonthLanBytes,
        PendingLanCarryBytes = state.PendingLanCarryBytes,
        DayFirstPollAtUtc = state.DayFirstPollAtUtc, DayHasSamplingGap = state.DayHasSamplingGap,
        CurrentDayBytes = state.CurrentDayBytes
    };
}
