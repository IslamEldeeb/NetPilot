using LiteDB;
using NetPilot.Core.Settings;

namespace NetPilot.Data;

public sealed class LitePreferencesStore(NetPilotDatabase db) : IPreferencesStore, ISetupProgressStore
{
    private readonly ILiteCollection<PreferencesDocument> _preferences = db.GetCollection<PreferencesDocument>("ui_preferences");
    public Task<NotificationPreferences?> GetNotificationsAsync(CancellationToken ct) => Task.FromResult(_preferences.FindById("household")?.Notifications);
    public Task<RetentionPreferences> GetRetentionAsync(CancellationToken ct) => Task.FromResult(_preferences.FindById("household")?.Retention ?? new());
    public Task<SetupProgress> GetAsync(CancellationToken ct) => Task.FromResult(_preferences.FindById("household")?.Setup ?? new());
    public Task SaveNotificationsAsync(NotificationPreferences preferences, CancellationToken ct)
    {
        preferences.Validate();
        return UpdateAsync(d => d.Notifications = preferences, ct);
    }
    public Task SaveRetentionAsync(RetentionPreferences preferences, CancellationToken ct)
    {
        preferences.Validate();
        return UpdateAsync(d => d.Retention = preferences, ct);
    }
    public Task SaveAsync(SetupProgress progress, CancellationToken ct)
    {
        if (progress.Step is < 0 or > 5) throw new ArgumentException("Invalid setup step.");
        return UpdateAsync(d => d.Setup = progress, ct);
    }
    public Task<bool> TryCreateDefaultQuotaAsync(long dailyBytes, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (dailyBytes <= 0) throw new ArgumentException("Daily cap must be positive.");
        var created = false;
        db.InTransaction(() =>
        {
            var quotas = new LiteQuotaStore(db);
            if (quotas.FindAsync("default", ct).GetAwaiter().GetResult() is not null) return;
            quotas.UpsertAsync(new() { Id = "default", DailyBytes = dailyBytes, DryRun = true, Action = Core.Quota.QuotaAction.NotifyOnly }, ct).GetAwaiter().GetResult();
            created = true;
        });
        return Task.FromResult(created);
    }
    private Task UpdateAsync(Action<PreferencesDocument> update, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        db.InTransaction(() =>
        {
            var document = _preferences.FindById("household") ?? new();
            update(document);
            _preferences.Upsert(document);
        });
        return Task.CompletedTask;
    }
    public sealed class PreferencesDocument
    {
        [BsonId] public string Id { get; set; } = "household";
        public NotificationPreferences? Notifications { get; set; }
        public RetentionPreferences? Retention { get; set; }
        public SetupProgress? Setup { get; set; }
    }
}
