using NetPilot.Core.Activity;
using NetPilot.Core.Enforcement;
using NetPilot.Core.Notifications;
using NetPilot.Core.Usage;
using NetPilot.Data.Documents;

namespace NetPilot.Data.Tests;

public sealed class ActivityFeedTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"netpilot-d1-feed-{Guid.NewGuid()}.db");
    private readonly NetPilotDatabase _db;
    private readonly LiteActivityLogStore _feed;
    public ActivityFeedTests() { _db = new(_path); _feed = new(_db); }

    [Fact]
    public async Task LegacyRows_RenderWithoutMigration_AndParticipateInStructuredFilters()
    {
        _db.GetCollection<ActivityLogDocument>("activity_log").Insert(new ActivityLogDocument { Type = "QuotaDryRun", AtUtc = DateTimeOffset.UtcNow, Message = "Would throttle" });
        _db.GetCollection<ActivityLogDocument>("activity_log").Insert(new ActivityLogDocument { Type = "RuleFailed", AtUtc = DateTimeOffset.UtcNow, Message = "Router unavailable" });
        var dry = Assert.Single((await _feed.QueryAsync(new() { Kind = "quota", DryRun = true }, default)).Entries);
        Assert.True(dry.EventId > 0); Assert.Equal("legacy", dry.Source); Assert.Null(dry.ReadAtUtc);
        Assert.True(Assert.Single((await _feed.QueryAsync(new() { FailedOnly = true }, default)).Entries).Failed);
        Assert.Equal(0, await _feed.UnreadCountAsync(default));
    }

    [Fact]
    public async Task KeysetPaging_DoesNotRepeatOrLoseEntriesWhenNewRowsArrive()
    {
        for (var index = 0; index < 5; index++) await _feed.AppendAsync(new(DateTimeOffset.UtcNow, ActivityEventType.DeviceDiscovered, null, index.ToString()), default);
        var first = await _feed.QueryAsync(new() { PageSize = 2 }, default);
        await _feed.AppendAsync(new(DateTimeOffset.UtcNow, ActivityEventType.DeviceDiscovered, null, "new"), default);
        var second = await _feed.QueryAsync(new() { PageSize = 2, BeforeId = first.NextId }, default);
        var third = await _feed.QueryAsync(new() { PageSize = 2, BeforeId = second.NextId }, default);
        var ids = first.Entries.Concat(second.Entries).Concat(third.Entries).Select(e => e.EventId).ToArray();
        Assert.Equal(5, ids.Length); Assert.Equal(5, ids.Distinct().Count()); Assert.Null(third.NextId);
        Assert.Equal(first.Entries[0], await _feed.FindAsync(first.Entries[0].EventId, default));
    }

    [Fact]
    public async Task NotificationEvent_DeduplicatesAcrossHandles_ReadStateSurvivesReopen_AndPruneKeepsUnread()
    {
        var entry = new ActivityLogEntry(DateTimeOffset.UtcNow.AddDays(-90), ActivityEventType.NotificationCreated, null, "Cap reached")
            { IsNotification = true, DeduplicationKey = "quota:one", Source = "notifications" };
        using var otherDb = new NetPilotDatabase(_path);
        var other = new LiteActivityLogStore(otherDb);
        await Task.WhenAll(Task.Run(() => _feed.AppendAsync(entry, default)), Task.Run(() => other.AppendAsync(entry, default)));
        Assert.Equal(1, await other.UnreadCountAsync(default));
        await _feed.PruneAsync(DateTimeOffset.UtcNow.AddDays(-62), default);
        var notification = Assert.Single((await _feed.QueryAsync(new() { Inbox = true }, default)).Entries);
        await other.MarkReadAsync(notification.EventId, default);
        Assert.Equal(0, await _feed.UnreadCountAsync(default));
        using var reopened = new NetPilotDatabase(_path);
        Assert.NotNull((await new LiteActivityLogStore(reopened).FindAsync(notification.EventId, default))!.ReadAtUtc);
        await _feed.PruneAsync(DateTimeOffset.UtcNow.AddDays(-62), default);
        Assert.Empty((await _feed.QueryAsync(new(), default)).Entries);
    }

    [Fact]
    public async Task NotificationDispatcher_CreatesOneInboxEventForMultipleAttempts_WithoutActionsOrUrls()
    {
        var dispatcher = new NotificationDispatcher([new Channel("one"), new Channel("two")], new LiteNotificationLogStore(_db), new UsagePeriodClock(), new NotificationOptions(), _feed);
        var notification = new NotificationMessage("Quota", "Review https://example.test/?token=topsecret Authorization: Bearer bearer-secret \"password\":\"two secret words\"", "/?token=another-secret", "same-event", [new("restore", "Restore", "consumed-secret")]);
        await dispatcher.DispatchAsync(notification);
        await dispatcher.DispatchAsync(notification);
        Assert.Equal(1, await _feed.UnreadCountAsync(default));
        var entry = Assert.Single((await _feed.QueryAsync(new() { Inbox = true }, default)).Entries);
        Assert.DoesNotContain("secret", entry.Message); Assert.Contains("[link removed]", entry.Message);
        Assert.Equal(4, (await new LiteNotificationLogStore(_db).GetRecentAsync(20, default)).Count);
    }

    public void Dispose() { _db.Dispose(); File.Delete(_path); }
    private sealed class Channel(string id) : INotificationChannel
    {
        public string Id => id;
        public Task SendAsync(NotificationMessage message, CancellationToken ct) => Task.CompletedTask;
    }
}
