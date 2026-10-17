using NetPilot.Core.Notifications;
using NetPilot.Core.Settings;
using NetPilot.Core.Usage;
using static NetPilot.Core.Tests.Notifications.NotificationDispatcherTests;

namespace NetPilot.Core.Tests.Notifications;

public sealed class NotificationPreferenceTests
{
    [Fact]
    public async Task ExistingDispatchersReadPreferenceChangesOnEveryAttempt()
    {
        var preferences = new Preferences();
        var channel = new RecordingChannel("push");
        var log = new MemoryLog();
        var dispatcher = new NotificationDispatcher([channel], log, new(), new(), preferences);
        var message = new NotificationMessage("Title", "Body", "/", "same-tag", []) { Event = NotificationEvent.QuotaExceeded };
        preferences.Notifications = new() { DisabledEvents = [NotificationEvent.QuotaExceeded] };
        await dispatcher.DispatchAsync(message);
        Assert.Equal(NotificationOutcome.SuppressedPreference, Assert.Single(log.Entries).Outcome);
        preferences.Notifications = new();
        await dispatcher.DispatchAsync(message);
        Assert.Equal(1, channel.Sends); // suppressed events do not consume dedupe
        preferences.Notifications = new() { Enabled = false };
        await dispatcher.DispatchAsync(message with { Tag = "other" });
        Assert.Equal(1, channel.Sends);
    }

    [Fact]
    public async Task SharedQuietHoursOverrideDeployment_ImmediateStillBypasses_AndRetentionIsShared()
    {
        var time = new MutableTimeProvider { Now = DateTimeOffset.Parse("2026-01-15T22:00:00Z") };
        var preferences = new Preferences { Notifications = new() { QuietEnabled = true }, Retention = new() { NotificationAttempts = 20 } };
        var channel = new RecordingChannel("push"); var log = new MemoryLog();
        var dispatcher = new NotificationDispatcher([channel], log, new UsagePeriodClock("Africa/Cairo", time), new(), preferences);
        for (var i = 0; i < 22; i++) await dispatcher.DispatchAsync(new("T", "B", "/", i.ToString(), []), NotificationInterruption.QuietHoursEligible, default);
        Assert.Equal(0, channel.Sends); Assert.Equal(20, log.Entries.Count);
        await dispatcher.DispatchAsync(new("T", "B", "/", "immediate", []));
        Assert.Equal(1, channel.Sends); Assert.Equal(NotificationOutcome.Sent, log.Entries[^1].Outcome);
    }

    private sealed class Preferences : IPreferencesStore
    {
        public NotificationPreferences? Notifications;
        public RetentionPreferences Retention = new();
        public Task<NotificationPreferences?> GetNotificationsAsync(CancellationToken ct) => Task.FromResult(Notifications);
        public Task SaveNotificationsAsync(NotificationPreferences preferences, CancellationToken ct) { Notifications = preferences; return Task.CompletedTask; }
        public Task<RetentionPreferences> GetRetentionAsync(CancellationToken ct) => Task.FromResult(Retention);
        public Task SaveRetentionAsync(RetentionPreferences preferences, CancellationToken ct) { Retention = preferences; return Task.CompletedTask; }
    }
}
