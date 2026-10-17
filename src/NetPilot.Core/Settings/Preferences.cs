using NetPilot.Core.Notifications;

namespace NetPilot.Core.Settings;

public enum NotificationEvent { General, QuotaWarning, QuotaExceeded, QuotaApplied, QuotaReverted, QuotaDryRun, QuotaProtected, Rule }

public sealed record NotificationPreferences
{
    public bool Enabled { get; init; } = true;
    public bool QuietEnabled { get; init; }
    public string QuietStart { get; init; } = "23:00";
    public string QuietEnd { get; init; } = "07:00";
    public NotificationEvent[] DisabledEvents { get; init; } = [];

    public bool Allows(NotificationEvent kind) => Enabled && !DisabledEvents.Contains(kind);
    public QuietHoursOptions QuietHours => new() { Enabled = QuietEnabled, Start = QuietStart, End = QuietEnd };
    public void Validate()
    {
        QuietHours.Validate();
        if (DisabledEvents is null || DisabledEvents.Any(e => !Enum.IsDefined(e)))
            throw new ArgumentException("Choose supported notification events.");
    }
    public static NotificationPreferences From(NotificationOptions options) => new()
    { QuietEnabled = options.QuietHours.Enabled, QuietStart = options.QuietHours.Start, QuietEnd = options.QuietHours.End };
}

public sealed record RetentionPreferences
{
    public int NotificationAttempts { get; init; } = 500;
    public int InactiveQuotaDays { get; init; } = 62;
    public int RuleFirings { get; init; } = 1000;
    public void Validate()
    {
        if (NotificationAttempts is < 20 or > 10000 || InactiveQuotaDays is < 62 or > 3650 || RuleFirings is < 100 or > 10000)
            throw new ArgumentException("Keep 20–10000 notification attempts, 62–3650 days of inactive quotas, and 100–10000 rule firings.");
    }
}

public interface IPreferencesStore
{
    Task<NotificationPreferences?> GetNotificationsAsync(CancellationToken ct);
    Task SaveNotificationsAsync(NotificationPreferences preferences, CancellationToken ct);
    Task<RetentionPreferences> GetRetentionAsync(CancellationToken ct);
    Task SaveRetentionAsync(RetentionPreferences preferences, CancellationToken ct);
}

// Only the step is persisted. Router secrets use the existing encrypted connection store.
public sealed record SetupProgress(int Step = 0, bool Completed = false);
public interface ISetupProgressStore
{
    Task<SetupProgress> GetAsync(CancellationToken ct);
    Task SaveAsync(SetupProgress progress, CancellationToken ct);
    Task<bool> TryCreateDefaultQuotaAsync(long dailyBytes, CancellationToken ct);
}
