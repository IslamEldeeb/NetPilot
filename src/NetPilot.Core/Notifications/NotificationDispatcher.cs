using NetPilot.Core.Usage;
using NetPilot.Core.Settings;

namespace NetPilot.Core.Notifications;

public sealed class NotificationDispatcher
{
    private readonly ChannelState[] _channels;
    private readonly INotificationLogStore _log;
    private readonly UsagePeriodClock _clock;
    private readonly NotificationOptions _options;
    private readonly IPreferencesStore? _preferences;

    public NotificationDispatcher(IEnumerable<INotificationChannel> channels, INotificationLogStore log,
        UsagePeriodClock clock, NotificationOptions options, IPreferencesStore? preferences = null)
    {
        options.Validate();
        _channels = channels.Select(c => new ChannelState(c)).ToArray();
        _log = log;
        _clock = clock;
        _options = options;
        _preferences = preferences;
    }

    public Task DispatchAsync(NotificationMessage message, CancellationToken ct = default) =>
        DispatchAsync(message, NotificationInterruption.Immediate, ct);

    public async Task DispatchAsync(NotificationMessage message, NotificationInterruption interruption, CancellationToken ct)
    {
        var preferences = await (_preferences?.GetNotificationsAsync(ct) ?? Task.FromResult<NotificationPreferences?>(null))
            ?? NotificationPreferences.From(_options);
        await Task.WhenAll(_channels.Select(channel => SendToChannelAsync(channel, message, interruption, preferences, ct)));
        var retention = _preferences is null ? new RetentionPreferences() : await _preferences.GetRetentionAsync(ct);
        await _log.PruneAsync(retention.NotificationAttempts, ct);
    }

    private async Task SendToChannelAsync(ChannelState state, NotificationMessage message,
        NotificationInterruption interruption, NotificationPreferences preferences, CancellationToken ct)
    {
        await state.Gate.WaitAsync(ct);
        try
        {
            var now = _clock.GetUtcNow();
            state.ExpireBefore(now - TimeSpan.FromHours(_options.DedupeWindowHours));
            var (outcome, error) = await AttemptAsync(state, message, interruption, preferences, ct);
            await _log.AppendAsync(new(now, message.Tag, message.Title, state.Channel.Id, outcome, error), ct);
        }
        finally { state.Gate.Release(); }
    }

    private Task<(NotificationOutcome Outcome, string? Error)> AttemptAsync(ChannelState state,
        NotificationMessage message, NotificationInterruption interruption, NotificationPreferences preferences, CancellationToken ct)
    {
        if (!preferences.Allows(message.Event))
            return Task.FromResult<(NotificationOutcome, string?)>((NotificationOutcome.SuppressedPreference, null));
        if (interruption == NotificationInterruption.QuietHoursEligible && IsQuiet(_clock.GetUtcNow(), preferences.QuietHours))
            return Task.FromResult<(NotificationOutcome, string?)>((NotificationOutcome.SuppressedQuietHours, null));
        if (state.Sent.ContainsKey(message.Tag))
            return Task.FromResult<(NotificationOutcome, string?)>((NotificationOutcome.SuppressedDuplicate, null));
        return DeliverAsync(state, message, ct);
    }

    private async Task<(NotificationOutcome Outcome, string? Error)> DeliverAsync(ChannelState state,
        NotificationMessage message, CancellationToken ct)
    {
        try
        {
            await state.Channel.SendAsync(message, ct);
            state.Sent[message.Tag] = _clock.GetUtcNow();
            return (NotificationOutcome.Sent, null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            // Channel failures are isolated; transport exception messages may contain credentials.
            return (NotificationOutcome.Failed, ex.GetType().Name);
        }
    }

    private bool IsQuiet(DateTimeOffset now, QuietHoursOptions hours)
    {
        if (!hours.Enabled) return false;
        var local = TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, _clock.TimeZone).DateTime);
        // Start is inclusive, end exclusive; equal endpoints disable the window.
        return hours.StartTime < hours.EndTime
            ? local >= hours.StartTime && local < hours.EndTime
            : hours.StartTime > hours.EndTime && (local >= hours.StartTime || local < hours.EndTime);
    }

    private sealed class ChannelState(INotificationChannel channel)
    {
        public INotificationChannel Channel { get; } = channel;
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public Dictionary<string, DateTimeOffset> Sent { get; } = new(StringComparer.Ordinal);

        public void ExpireBefore(DateTimeOffset cutoff)
        {
            foreach (var tag in Sent.Where(p => p.Value <= cutoff).Select(p => p.Key).ToArray())
                Sent.Remove(tag);
        }
    }
}

public enum NotificationInterruption { Immediate, QuietHoursEligible }
