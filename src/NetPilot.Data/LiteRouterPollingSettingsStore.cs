using LiteDB;
using NetPilot.Core.Health;

namespace NetPilot.Data;

public sealed class LiteRouterPollingSettingsStore(NetPilotDatabase db) : IRouterPollingSettingsStore
{
    private readonly ILiteCollection<PollingDocument> _settings = db.GetCollection<PollingDocument>("router_polling_settings");
    public Task<int?> GetIntervalSecondsAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_settings.FindById("router")?.IntervalSeconds);
    }
    public Task SaveIntervalSecondsAsync(int seconds, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        RouterPollingSettings.Validate(seconds);
        _settings.Upsert(new PollingDocument { IntervalSeconds = seconds });
        return Task.CompletedTask;
    }
    public sealed class PollingDocument
    {
        [BsonId] public string Id { get; set; } = "router";
        public int IntervalSeconds { get; set; }
    }
}
