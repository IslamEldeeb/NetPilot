using NetPilot.Core.Notifications;
using NetPilot.Core.Quota;
using NetPilot.Core.Usage;
using static NetPilot.Core.Tests.Notifications.NotificationDispatcherTests;

namespace NetPilot.Core.Tests.Notifications;

public class NotificationQuotaEventSinkTests
{
    private static QuotaEvent Event(QuotaEventKind kind) => new(kind, new("AA:BB:CC:DD:EE:01"), "Phone",
        4_800_000_000, 5_000_000_000, 96, QuotaPeriodKind.Day, "2026-01-15", QuotaAction.Throttle, false,
        DateTimeOffset.Parse("2026-01-15T22:00:00Z"), 512, 80);

    [Theory]
    [InlineData(QuotaEventKind.Warning, "Phone: 4.8 GB of 5 GB used today (96%)", "Warning:80")]
    [InlineData(QuotaEventKind.Exceeded, "Phone reached its daily limit", "Exceeded")]
    [InlineData(QuotaEventKind.DryRun, "[Dry-run] would have throttled Phone to 512 Kbps", "DryRun")]
    [InlineData(QuotaEventKind.ActionApplied, "Phone throttled to 512 Kbps", "ActionApplied")]
    [InlineData(QuotaEventKind.Reverted, "Phone restored", "Reverted")]
    [InlineData(QuotaEventKind.ActionSkippedProtected, "Phone is over quota but protected - no action taken", "ActionSkippedProtected")]
    public void EveryQuotaEventHasStablePeriodSpecificTagAndExpectedBody(QuotaEventKind kind, string body, string suffix)
    {
        var quotaEvent = Event(kind);
        var message = NotificationQuotaEventSink.ToMessage(quotaEvent);
        Assert.Equal(body, message.Body);
        Assert.Equal($"quota:AA-BB-CC-DD-EE-01:Day:2026-01-15:{suffix}", message.Tag);
        Assert.Equal(message.Tag, NotificationQuotaEventSink.ToMessage(quotaEvent with { AtUtc = quotaEvent.AtUtc.AddMinutes(1) }).Tag);
        Assert.Equal("NetPilot quota", message.Title);
        Assert.Equal("/", message.Url);
        Assert.Empty(message.Actions);
    }

    [Fact]
    public void MonthlyWarningsAndBlockMessagesUseCorrectUnitsAndDistinctTags()
    {
        var warning = Event(QuotaEventKind.Warning) with { PeriodKind = QuotaPeriodKind.Month, PeriodKey = "2026-01", UsedBytes = 480_000_000, CapBytes = 600_000_000, Percent = 80 };
        var message = NotificationQuotaEventSink.ToMessage(warning);
        Assert.Equal("Phone: 480 MB of 600 MB used this month (80%)", message.Body);
        Assert.Contains(":Month:2026-01:", message.Tag);
        Assert.NotEqual(message.Tag, NotificationQuotaEventSink.ToMessage(warning with { WarnPercent = 90 }).Tag);
        Assert.NotEqual(message.Tag, NotificationQuotaEventSink.ToMessage(warning with { PeriodKey = "2026-02" }).Tag);
        Assert.Equal("Phone reached its monthly limit", NotificationQuotaEventSink.ToMessage(warning with { Kind = QuotaEventKind.Exceeded }).Body);
        Assert.Equal("Phone blocked", NotificationQuotaEventSink.ToMessage(Event(QuotaEventKind.ActionApplied) with { Action = QuotaAction.Block }).Body);
        Assert.Equal("[Dry-run] would have blocked Phone", NotificationQuotaEventSink.ToMessage(Event(QuotaEventKind.DryRun) with { Action = QuotaAction.Block }).Body);
        Assert.Equal("[Dry-run] would have notified for Phone", NotificationQuotaEventSink.ToMessage(Event(QuotaEventKind.DryRun) with { Action = QuotaAction.NotifyOnly }).Body);
    }

    [Theory]
    [InlineData(QuotaEventKind.Warning, true)]
    [InlineData(QuotaEventKind.DryRun, true)]
    [InlineData(QuotaEventKind.Exceeded, false)]
    [InlineData(QuotaEventKind.ActionApplied, false)]
    [InlineData(QuotaEventKind.Reverted, false)]
    [InlineData(QuotaEventKind.ActionSkippedProtected, false)]
    public async Task QuietHoursSuppressOnlyWarningsAndDryRunEvents_EvenWhenExceededEventHasDryRunFlag(QuotaEventKind kind, bool suppressed)
    {
        var channel = new RecordingChannel("push");
        var log = new MemoryLog();
        var dispatcher = new NotificationDispatcher([channel], log,
            new UsagePeriodClock("Africa/Cairo", new MutableTimeProvider { Now = Event(kind).AtUtc }),
            new NotificationOptions { QuietHours = new() { Enabled = true } });
        await new NotificationQuotaEventSink(dispatcher).PublishAsync(Event(kind) with { DryRun = true }, CancellationToken.None);
        Assert.Equal(suppressed ? 0 : 1, channel.Sends);
        Assert.Equal(suppressed ? NotificationOutcome.SuppressedQuietHours : NotificationOutcome.Sent, Assert.Single(log.Entries).Outcome);
    }

    [Fact]
    public void SubscriptionIdentityDependsOnlyOnExactEndpoint()
    {
        var endpoint = "https://push.example/subscription/one";
        Assert.Equal(PushSubscriptionInfo.IdForEndpoint(endpoint), PushSubscriptionInfo.IdForEndpoint(new string(endpoint.ToCharArray())));
        Assert.NotEqual(PushSubscriptionInfo.IdForEndpoint(endpoint), PushSubscriptionInfo.IdForEndpoint(endpoint + "two"));
        Assert.Equal(64, PushSubscriptionInfo.IdForEndpoint(endpoint).Length);
    }
}
