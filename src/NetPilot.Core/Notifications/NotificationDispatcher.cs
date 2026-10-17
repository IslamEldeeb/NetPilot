using NetPilot.Core.Usage;
using NetPilot.Core.Enforcement;
using NetPilot.Core.Activity;

namespace NetPilot.Core.Notifications;

public sealed class NotificationDispatcher
{
    private readonly ChannelState[] _channels;
    private readonly INotificationLogStore _log;
    private readonly UsagePeriodClock _clock;
    private readonly NotificationOptions _options;
    private readonly IActivityLogStore? _activity;

    public NotificationDispatcher(IEnumerable<INotificationChannel> channels, INotificationLogStore log,
        UsagePeriodClock clock, NotificationOptions options, IActivityLogStore? activity = null)
    {
        options.Validate();
        _activity = activity;
        _channels = channels.Select(c => new ChannelState(c)).ToArray();
        _log = log;
        _clock = clock;
        _options = options;
    }

    public Task DispatchAsync(NotificationMessage message, CancellationToken ct = default) =>
        DispatchAsync(message, NotificationInterruption.Immediate, ct);

    public async Task DispatchAsync(NotificationMessage message, NotificationInterruption interruption, CancellationToken ct)
    {
        if (_activity is not null)
        {
            await _activity.AppendAsync(new(_clock.GetUtcNow(), ActivityEventType.NotificationCreated, message.Mac,
                ActivityPrivacy.Redact(message.Title + ": " + message.Body))
            {
                Source = "notifications", Kind = "notification", IsNotification = true,
                DeduplicationKey = "notification:" + message.Tag, CorrelationId = message.CorrelationId, DryRun = message.DryRun
            }, ct);
            if (_activity is IActivityFeed feed) await feed.PruneAsync(_clock.GetUtcNow().AddDays(-62), ct);
        }
        await Task.WhenAll(_channels.Select(channel => SendToChannelAsync(channel, message, interruption, ct)));
        await _log.PruneAsync(500, ct);
    }

    private async Task SendToChannelAsync(ChannelState state, NotificationMessage message,
        NotificationInterruption interruption, CancellationToken ct)
    {
        await state.Gate.WaitAsync(ct);
        try
        {
            var now = _clock.GetUtcNow();
            state.ExpireBefore(now - TimeSpan.FromHours(_options.DedupeWindowHours));
            var (outcome, error) = await AttemptAsync(state, message, interruption, ct);
            await _log.AppendAsync(new(now, message.Tag, message.Title, state.Channel.Id, outcome, error), ct);
        }
        finally { state.Gate.Release(); }
    }

    private Task<(NotificationOutcome Outcome, string? Error)> AttemptAsync(ChannelState state,
        NotificationMessage message, NotificationInterruption interruption, CancellationToken ct)
    {
        if (interruption == NotificationInterruption.QuietHoursEligible && IsQuiet(_clock.GetUtcNow()))
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

    private bool IsQuiet(DateTimeOffset now)
    {
        var hours = _options.QuietHours;
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
