using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetPilot.Core.Devices;
using NetPilot.Core.Rules;
using NetPilot.Data.Documents;

namespace NetPilot.Data.Tests;

public class LiteRuleStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"netpilot-rules-{Guid.NewGuid():N}");
    private string DbPath => Path.Combine(_directory, "test.db");
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly MacAddress Mac = new("aa:bb:cc:dd:ee:01");
    public LiteRuleStoreTests() => Directory.CreateDirectory(_directory);

    [Theory]
    [InlineData("day", RuleRevertKind.None)]
    [InlineData("month", RuleRevertKind.AtPeriodEnd)]
    [InlineData("household", RuleRevertKind.AfterMinutes)]
    [InlineData("window", RuleRevertKind.WhenTriggerEnds)]
    [InlineData("joined", RuleRevertKind.None)]
    [InlineData("idle", RuleRevertKind.AtPeriodEnd)]
    public async Task EveryTriggerAndAction_RoundTripsAcrossReopen(string kind, RuleRevertKind revert)
    {
        RuleTrigger trigger = kind switch
        {
            "day" => new UsageOver(RulePeriod.Day, 1000),
            "month" => new UsageOver(RulePeriod.Month, 2000),
            "household" => new HouseholdUsageOver(RulePeriod.Month, long.MaxValue),
            "window" => new TimeWindow("22:00", "07:00", [DayOfWeek.Monday, DayOfWeek.Saturday]),
            "joined" => new DeviceJoined(),
            _ => new DeviceIdle(14)
        };
        var rule = new Rule { Name = "Round trip", DryRun = false, Trigger = trigger,
            Scope = new() { Kind = RuleScopeKind.Devices, Devices = [Mac, new("aa:bb:cc:dd:ee:02")] },
            Actions = [new Notify("{rule}", "{device} {unknown}"), new Throttle(128), new Block(),
                new SetWireless("guest-5g", false), new SetWireless("iot-2.4g", true)],
            Revert = new(revert, 15), OncePer = RuleOncePer.Month, CooldownMinutes = 17,
            CreatedAtUtc = new(2026, 10, 15, 12, 34, 56, TimeSpan.Zero), UpdatedAtUtc = new(2026, 10, 15, 13, 34, 56, TimeSpan.Zero) };
        string originalPayload;
        using (var db = new NetPilotDatabase(DbPath))
        {
            await new LiteRuleStore(db).UpsertAsync(rule, Ct);
            originalPayload = db.GetCollection<RuleDocument>("rules").FindById(rule.Id.ToString()).Payload;
        }
        using var reopened = new NetPilotDatabase(DbPath);
        var store = new LiteRuleStore(reopened);
        var loaded = (await store.FindAsync(rule.Id, Ct))!;
        Assert.Equal(rule.Actions, loaded.Actions);
        Assert.Equal(rule.Scope.Devices, loaded.Scope.Devices);
        Assert.Equal(trigger.GetType(), loaded.Trigger.GetType());
        if (trigger is TimeWindow window) Assert.Equal(window.Days, Assert.IsType<TimeWindow>(loaded.Trigger).Days);
        else Assert.Equal(trigger, loaded.Trigger);
        await store.UpsertAsync(loaded, Ct);
        Assert.Equal(originalPayload, reopened.GetCollection<RuleDocument>("rules").FindById(rule.Id.ToString()).Payload);
        await store.DeleteAsync(rule.Id, Ct);
        Assert.Empty(await store.GetAllAsync(Ct));
    }

    [Theory]
    [InlineData("device-joined", "future-trigger")]
    [InlineData("device-joined", "DeviceJoinedV0")]
    [InlineData("notify", "future-action")]
    public async Task UnknownOrOldDiscriminator_IsSkippedAndLogged_WithoutHidingValidRules(string oldType, string newType)
    {
        using var db = new NetPilotDatabase(DbPath);
        var logger = new RecordingLogger();
        var store = new LiteRuleStore(db, logger);
        var bad = new Rule { Name = "Unknown", Actions = [new Notify("title", "body")] };
        var good = bad with { Id = Guid.NewGuid(), Name = "Valid" };
        await store.UpsertAsync(bad, Ct);
        await store.UpsertAsync(good, Ct);
        var rows = db.GetCollection<RuleDocument>("rules");
        var doc = rows.FindById(bad.Id.ToString());
        doc.Payload = doc.Payload.Replace($"\"type\":\"{oldType}\"", $"\"type\":\"{newType}\"", StringComparison.Ordinal);
        rows.Upsert(doc);
        Assert.Null(await store.FindAsync(bad.Id, Ct));
        Assert.Equal(good.Id, Assert.Single(await store.GetAllAsync(Ct)).Id);
        Assert.NotEmpty(logger.Warnings);
    }

    [Fact]
    public async Task RegistrationProvidesStores_AndValidationPreventsUnsafeWirelessPersistence()
    {
        using var services = new ServiceCollection().AddNetPilotData(DbPath, Path.Combine(_directory, "keys")).BuildServiceProvider();
        var store = services.GetRequiredService<IRuleStore>();
        Assert.Empty(await store.GetAllAsync(Ct));
        Assert.Empty(await services.GetRequiredService<IRuleFiringStore>().GetAllAsync(Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => store.UpsertAsync(new()
        { Name = "Unsafe", Actions = [new SetWireless("main-5g", false)] }, Ct));
        Assert.Empty(await store.GetAllAsync(Ct));
    }

    [Fact]
    public async Task Firings_PreserveRecoveryDataAndWebReleaseRequestAcrossSharedHandles()
    {
        var firing = new RuleFiring { RuleId = Guid.NewGuid(), RuleName = "Rule", Subject = Mac.ToString(), EpisodeKey = "episode",
            FiredAtUtc = DateTimeOffset.UtcNow, TargetMacs = [Mac], Actions = [new Throttle(128), new SetWireless("guest-5g", false)],
            AppliedClaimIds = [$"{Mac}|rule:test|Throttle"], WirelessPrior = [new("guest-5g", true, false)],
            Window = new("22:00", "07:00", [DayOfWeek.Friday]), RevertWhenTriggerEnds = true,
            RevertAtUtc = DateTimeOffset.UtcNow.AddMinutes(5), RevertPeriod = RulePeriod.Day, RevertPeriodKey = "2026-10-15",
            CompletedActionKeys = ["0:device"], Error = "retry", Outcome = RuleFiringOutcome.Failed };
        using (var db = new NetPilotDatabase(DbPath))
            await new LiteRuleFiringStore(db).UpdateAsync(firing.Id, _ => firing, Ct);
        using var reopened = new NetPilotDatabase(DbPath);
        using var shared = new NetPilotDatabase(DbPath);
        var reader = new LiteRuleFiringStore(reopened);
        var writer = new LiteRuleFiringStore(shared);
        await writer.UpdateAsync(firing.Id, current => current! with { ReleaseRequested = true }, Ct);
        var loaded = (await reader.FindAsync(firing.Id, Ct))!;
        Assert.True(loaded.ReleaseRequested);
        Assert.Equal(firing.Actions, loaded.Actions);
        Assert.Equal(firing.AppliedClaimIds, loaded.AppliedClaimIds);
        Assert.Equal(firing.WirelessPrior, loaded.WirelessPrior);
        Assert.Equal(firing.TargetMacs, loaded.TargetMacs);
        Assert.Equal(firing.Window.Days, loaded.Window!.Days);
        Assert.Equal(firing.RevertAtUtc, loaded.RevertAtUtc);
        Assert.Equal(firing.RevertPeriodKey, loaded.RevertPeriodKey);
        Assert.Equal(firing.CompletedActionKeys, loaded.CompletedActionKeys);
        Assert.Equal(firing.Error, loaded.Error);
        Assert.True((await reader.FindLatestAsync(firing.RuleId, firing.Subject, Ct))!.ReleaseRequested);
    }

    [Fact]
    public async Task Prune_KeepsActiveFiringsAndNewestHistory_AndRetainsSuppressionAfterAllHistoryRemoved()
    {
        using var db = new NetPilotDatabase(DbPath);
        var store = new LiteRuleFiringStore(db);
        var start = new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);
        var ruleId = Guid.NewGuid();
        for (var i = 0; i < 8; i++)
        {
            var firing = new RuleFiring { RuleId = ruleId, Subject = Mac.ToString(), EpisodeKey = i.ToString(),
                FiredAtUtc = start.AddMinutes(i), Status = i < 2 ? RuleFiringStatus.Active : RuleFiringStatus.Released };
            await store.UpdateAsync(firing.Id, _ => firing, Ct);
        }
        await store.PruneAsync(3, Ct);
        var retained = await store.GetAllAsync(Ct);
        Assert.Equal(5, retained.Count);
        Assert.Equal(2, retained.Count(f => f.Status == RuleFiringStatus.Active));
        Assert.Equal(new[] { "5", "6", "7" }, retained.Where(f => f.Status != RuleFiringStatus.Active).Select(f => f.EpisodeKey).Order());
        await store.PruneAsync(0, Ct);
        Assert.Equal(2, (await store.GetAllAsync(Ct)).Count);
        Assert.Equal("7", (await store.FindLatestAsync(ruleId, Mac, Ct))!.EpisodeKey);
        // Updating an older active record must not rewind the subject's firing watermark.
        var older = retained[0];
        await store.UpdateAsync(older.Id, current => current! with { ReleaseRequested = true }, Ct);
        Assert.Equal("7", (await store.FindLatestAsync(ruleId, Mac, Ct))!.EpisodeKey);
    }

    private sealed class RecordingLogger : ILogger<LiteRuleStore>
    {
        public List<string> Warnings { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        { if (logLevel == LogLevel.Warning) Warnings.Add(formatter(state, exception)); }
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
