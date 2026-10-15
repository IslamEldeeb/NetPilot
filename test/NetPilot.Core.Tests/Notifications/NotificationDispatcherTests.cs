using NetPilot.Core.Notifications;
using NetPilot.Core.Usage;

namespace NetPilot.Core.Tests.Notifications;

public class NotificationDispatcherTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly NotificationMessage Message = new("Title", "Body", "/", "quota:one", []);

    [Fact]
    public async Task ConcurrentRetriesAreDedupedUntilWindowExpires_AndDifferentTagsSend()
    {
        var clock = new MutableTimeProvider();
        var channel = new RecordingChannel("push");
        var log = new MemoryLog();
        var dispatcher = Create([channel], log, clock, new() { DedupeWindowHours = 2 });
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => dispatcher.DispatchAsync(Message, Ct)));
        Assert.Equal(1, channel.Sends);
        Assert.Equal(7, log.Entries.Count(e => e.Outcome == NotificationOutcome.SuppressedDuplicate));
        clock.Now = clock.Now.AddHours(2);
        await dispatcher.DispatchAsync(Message, Ct);
        await dispatcher.DispatchAsync(Message with { Tag = "other" }, Ct);
        Assert.Equal(3, channel.Sends);
        Assert.All(log.Entries, e => { Assert.Equal("push", e.ChannelId); Assert.Equal("Title", e.Title); });
    }

    [Fact]
    public async Task ZeroDedupeWindowAllowsRepeatedSends()
    {
        var channel = new RecordingChannel("push");
        var dispatcher = Create([channel], new(), new(), new() { DedupeWindowHours = 0 });
        await dispatcher.DispatchAsync(Message, Ct);
        await dispatcher.DispatchAsync(Message, Ct);
        Assert.Equal(2, channel.Sends);
    }

    [Theory]
    [InlineData("23:00", "07:00", "2026-01-15T20:59:00Z", false)]
    [InlineData("23:00", "07:00", "2026-01-15T21:00:00Z", true)]
    [InlineData("23:00", "07:00", "2026-01-16T00:30:00Z", true)]
    [InlineData("23:00", "07:00", "2026-01-16T05:00:00Z", false)]
    [InlineData("12:00", "13:00", "2026-01-15T10:00:00Z", true)]
    [InlineData("12:00", "13:00", "2026-01-15T11:00:00Z", false)]
    [InlineData("07:00", "07:00", "2026-01-15T05:00:00Z", false)]
    [InlineData("23:00", "07:00", "2026-07-15T20:00:00Z", true)]
    public async Task QuietHoursUseCairoTimeAndHalfOpenBoundaries(string start, string end, string utc, bool suppressed)
    {
        var channel = new RecordingChannel("push");
        var log = new MemoryLog();
        var dispatcher = Create([channel], log, new() { Now = DateTimeOffset.Parse(utc) },
            new() { QuietHours = new() { Enabled = true, Start = start, End = end } });
        await dispatcher.DispatchAsync(Message, NotificationInterruption.QuietHoursEligible, Ct);
        Assert.Equal(suppressed ? 0 : 1, channel.Sends);
        Assert.Equal(suppressed ? NotificationOutcome.SuppressedQuietHours : NotificationOutcome.Sent,
            Assert.Single(log.Entries).Outcome);
    }

    [Fact]
    public async Task SuppressedMessageCanSendAfterQuietHours_AndDisabledQuietHoursDoNotSuppress()
    {
        var clock = new MutableTimeProvider { Now = DateTimeOffset.Parse("2026-01-15T22:00:00Z") };
        var log = new MemoryLog();
        var channel = new RecordingChannel("push");
        var dispatcher = Create([channel], log, clock, new() { QuietHours = new() { Enabled = true } });
        await dispatcher.DispatchAsync(Message, NotificationInterruption.QuietHoursEligible, Ct);
        clock.Now = DateTimeOffset.Parse("2026-01-16T05:00:00Z");
        await dispatcher.DispatchAsync(Message, NotificationInterruption.QuietHoursEligible, Ct);
        Assert.Equal(1, channel.Sends);
        clock.Now = DateTimeOffset.Parse("2026-01-16T22:00:00Z");
        await Create([channel], log, clock, new()).DispatchAsync(Message, NotificationInterruption.QuietHoursEligible, Ct);
        Assert.Equal(2, channel.Sends);
    }

    [Fact]
    public async Task FailedChannelDoesNotPreventOthersOrDedupeItsOwnRetry_AndErrorsAreSanitized()
    {
        var failing = new RecordingChannel("bad") { Throw = true };
        var good = new RecordingChannel("good");
        var log = new MemoryLog();
        var dispatcher = Create([failing, good], log, new(), new());
        await dispatcher.DispatchAsync(Message, Ct);
        failing.Throw = false;
        await dispatcher.DispatchAsync(Message, Ct);
        Assert.Equal(2, failing.Sends);
        Assert.Equal(1, good.Sends);
        Assert.Equal("InvalidOperationException", Assert.Single(log.Entries, e => e.Outcome == NotificationOutcome.Failed).Error);
        Assert.Equal(2, log.Entries.Count(e => e.Outcome == NotificationOutcome.Sent));
        Assert.Single(log.Entries, e => e.Outcome == NotificationOutcome.SuppressedDuplicate);
    }

    [Fact]
    public async Task SlowChannelDoesNotDelayStartingAnotherChannel()
    {
        var slow = new WaitingChannel();
        var good = new RecordingChannel("good");
        var dispatcher = Create([slow, good], new(), new(), new());
        var delivery = dispatcher.DispatchAsync(Message, Ct);
        Assert.Equal(1, good.Sends);
        Assert.False(delivery.IsCompleted);
        slow.Completion.SetResult();
        await delivery;
    }

    [Fact]
    public async Task OnlyNewest500AttemptsAreKept()
    {
        var log = new MemoryLog();
        var dispatcher = Create([new RecordingChannel("push")], log, new(), new());
        for (var i = 0; i < 505; i++) await dispatcher.DispatchAsync(Message with { Tag = i.ToString() }, Ct);
        Assert.Equal(500, log.Entries.Count);
        Assert.Equal("5", log.Entries[0].Tag);
        Assert.Equal("504", log.Entries[^1].Tag);
    }

    private static NotificationDispatcher Create(INotificationChannel[] channels, MemoryLog log, MutableTimeProvider time, NotificationOptions options) =>
        new(channels, log, new UsagePeriodClock("Africa/Cairo", time), options);

    internal sealed class RecordingChannel(string id) : INotificationChannel
    {
        public string Id => id;
        public int Sends { get; private set; }
        public bool Throw { get; set; }
        public Task SendAsync(NotificationMessage message, CancellationToken ct)
        {
            Sends++;
            if (Throw) throw new InvalidOperationException("secret subscription keys");
            return Task.CompletedTask;
        }
    }

    private sealed class WaitingChannel : INotificationChannel
    {
        public string Id => "slow";
        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task SendAsync(NotificationMessage message, CancellationToken ct) => Completion.Task;
    }

    internal sealed class MutableTimeProvider : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.Parse("2026-01-15T12:00:00Z");
        public override DateTimeOffset GetUtcNow() => Now;
    }

    internal sealed class MemoryLog : INotificationLogStore
    {
        public List<NotificationLogEntry> Entries { get; } = [];
        public Task AppendAsync(NotificationLogEntry entry, CancellationToken ct) { lock (Entries) Entries.Add(entry); return Task.CompletedTask; }
        public Task<IReadOnlyList<NotificationLogEntry>> GetRecentAsync(int count, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<NotificationLogEntry>>(Entries.TakeLast(count).Reverse().ToArray());
        public Task PruneAsync(int keepNewest, CancellationToken ct)
        {
            lock (Entries) if (Entries.Count > keepNewest) Entries.RemoveRange(0, Entries.Count - keepNewest);
            return Task.CompletedTask;
        }
    }
}
