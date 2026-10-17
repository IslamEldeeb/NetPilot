using NetPilot.Core.Automations;
using NetPilot.Core.Enforcement;
using NetPilot.Core.Quota;
using NetPilot.Core.Rules;
using NetPilot.Core.Usage;

namespace NetPilot.Data.Tests;

public sealed class AutomationCommandsTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"netpilot-d1-undo-{Guid.NewGuid()}.db");
    private readonly TestTime _time = new();
    private readonly NetPilotDatabase _db;
    private readonly LiteRuleStore _rules;
    private readonly LiteQuotaStore _quotas;
    private readonly LiteAutomationCommands _commands;
    public AutomationCommandsTests()
    {
        _db = new(_path); _rules = new(_db); _quotas = new(_db);
        _commands = new(_db, _rules, _quotas, new LiteActivityLogStore(_db), new UsagePeriodClock(timeProvider: _time));
    }
    private static Rule Rule => new() { Name = "Bedtime", Actions = [new Throttle(256)] };

    [Fact]
    public async Task RuleEditAndDelete_UndoPersistAcrossReopen_WithoutResurrectingFirings()
    {
        var rule = Rule;
        await _rules.UpsertAsync(rule, default);
        var firings = new LiteRuleFiringStore(_db);
        var firing = new RuleFiring { RuleId = rule.Id, Subject = "household", EpisodeKey = "test", FiredAtUtc = _time.Now };
        await firings.UpdateAsync(firing.Id, _ => firing, default);
        var changed = rule with { Name = "Edited", Actions = [new Block()] };
        var receipt = await _commands.SaveRuleAsync(rule, changed, default);
        using var second = new NetPilotDatabase(_path);
        var reopened = new LiteAutomationCommands(second, new LiteRuleStore(second), new LiteQuotaStore(second), new LiteActivityLogStore(second), new UsagePeriodClock(timeProvider: _time));
        await firings.UpdateAsync(firing.Id, current => current! with { Status = RuleFiringStatus.Released }, default);
        Assert.Equal(UndoOutcome.Restored, await reopened.UndoAsync(receipt.Id, default));
        Assert.Equal("Bedtime", (await _rules.FindAsync(rule.Id, default))!.Name);
        Assert.Equal(RuleFiringStatus.Released, Assert.Single(await firings.GetAllAsync(default)).Status);
        Assert.Equal(UndoOutcome.AlreadyRestored, await _commands.UndoAsync(receipt.Id, default));
        var deletion = await _commands.SaveRuleAsync(await _rules.FindAsync(rule.Id, default), null, default);
        Assert.Null(await _rules.FindAsync(rule.Id, default));
        Assert.Equal(UndoOutcome.Restored, await reopened.UndoAsync(deletion.Id, default));
    }

    [Fact]
    public async Task QuotaRemoval_UndoRestoresAllSettings_AndStaleDraftCannotSave()
    {
        var quota = new QuotaPolicy { Id = "default", DailyBytes = 5_000_000_000, MonthlyBytes = 90_000_000_000,
            WarnPercents = [80, 90], Exempt = true, DryRun = false, Action = QuotaAction.Throttle, ThrottleKbps = 512 };
        await _quotas.UpsertAsync(quota, default);
        var receipt = await _commands.SaveQuotaAsync(quota, null, default);
        Assert.Equal(UndoOutcome.Restored, await _commands.UndoAsync(receipt.Id, default));
        var restored = (await _quotas.FindAsync("default", default))!;
        Assert.Equal(quota.WarnPercents, restored.WarnPercents); Assert.True(restored.Exempt); Assert.False(restored.DryRun);
        Assert.Equal(quota.MonthlyBytes, restored.MonthlyBytes); Assert.Equal(512, restored.ThrottleKbps);
        await _quotas.UpsertAsync(restored with { DailyBytes = 7_000_000_000 }, default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _commands.SaveQuotaAsync(restored, restored with { Exempt = false }, default));
        Assert.True((await _quotas.FindAsync("default", default))!.Exempt);
    }

    [Theory]
    [InlineData("edit")]
    [InlineData("delete-recreate")]
    [InlineData("same-value-write")]
    public async Task Undo_ConflictsWithInterveningWritesIncludingSameIntent(string kind)
    {
        var rule = Rule;
        var receipt = await _commands.SaveRuleAsync(null, rule, default);
        if (kind == "delete-recreate") await _rules.DeleteAsync(rule.Id, default);
        await _rules.UpsertAsync(kind == "edit" ? rule with { Name = "Another editor" } : rule, default);
        Assert.Equal(UndoOutcome.Conflict, await _commands.UndoAsync(receipt.Id, default));
        Assert.NotNull(await _rules.FindAsync(rule.Id, default));
    }

    [Fact]
    public async Task Undo_ExpiresAtTenSeconds_AndUnknownCommandCannotRestoreAnything()
    {
        var receipt = await _commands.SaveRuleAsync(null, Rule, default);
        _time.Now = _time.Now.AddSeconds(10);
        Assert.Equal(UndoOutcome.Expired, await _commands.UndoAsync(receipt.Id, default));
        Assert.Equal(UndoOutcome.NotFound, await _commands.UndoAsync("reboot-or-consumed-token", default));
        Assert.Single(await _rules.GetAllAsync(default));
    }

    [Fact]
    public async Task Import_RechecksConflictsAtomically_AndRollsBackTheWholeBatch()
    {
        var existing = Rule;
        await _rules.UpsertAsync(existing, default);
        await Assert.ThrowsAsync<ArgumentException>(() => _commands.ImportRulesAsync([Rule, existing], default));
        Assert.Single(await _rules.GetAllAsync(default));
        var rule = Rule with { DryRun = false };
        await _commands.ImportRulesAsync([rule], default);
        Assert.True((await _rules.FindAsync(rule.Id, default))!.DryRun);
        Assert.Equal(_time.Now, (await _rules.FindAsync(rule.Id, default))!.CreatedAtUtc);
    }

    [Fact]
    public async Task ConcurrentQuotaEditors_OnlyOneSaveWins()
    {
        var quota = new QuotaPolicy { Id = "default", DailyBytes = 1_000_000_000 };
        await _quotas.UpsertAsync(quota, default);
        using var otherDb = new NetPilotDatabase(_path);
        var other = new LiteAutomationCommands(otherDb, new LiteRuleStore(otherDb), new LiteQuotaStore(otherDb), new LiteActivityLogStore(otherDb), new UsagePeriodClock(timeProvider: _time));
        async Task<bool> Save(LiteAutomationCommands commands, long cap)
        {
            try { await commands.SaveQuotaAsync(quota, quota with { DailyBytes = cap }, default); return true; }
            catch (InvalidOperationException) { return false; }
        }
        var saved = await Task.WhenAll(Task.Run(() => Save(_commands, 2_000_000_000)), Task.Run(() => Save(other, 3_000_000_000)));
        Assert.Single(saved, success => success);
        var audit = await new LiteActivityLogStore(_db).GetRecentAsync(10, default);
        Assert.Single(audit); Assert.NotNull(audit[0].ChangeId);
    }

    [Fact]
    public async Task FailedAudit_RollsBackConfigurationAndSnapshot_AndConnectionRemainsUsable()
    {
        var failing = new LiteAutomationCommands(_db, _rules, _quotas, new FailingAudit(), new UsagePeriodClock(timeProvider: _time));
        var rule = Rule;
        await Assert.ThrowsAsync<IOException>(() => failing.SaveRuleAsync(null, rule, default));
        using var other = new NetPilotDatabase(_path);
        Assert.Null(await new LiteRuleStore(other).FindAsync(rule.Id, default));
        Assert.Equal(0, other.GetCollection<AutomationSnapshot>("automation_changes").Count());
        await _commands.SaveRuleAsync(null, rule, default);
        Assert.NotNull(await new LiteRuleStore(other).FindAsync(rule.Id, default));
    }

    private sealed class FailingAudit : IActivityLogStore
    {
        public Task AppendAsync(ActivityLogEntry entry, CancellationToken ct) => throw new IOException("Storage unavailable");
        public Task<IReadOnlyList<ActivityLogEntry>> GetRecentAsync(int count, CancellationToken ct) => throw new NotSupportedException();
    }

    public void Dispose() { _db.Dispose(); File.Delete(_path); File.Delete(Path.ChangeExtension(_path, "-log.db")); }
    private sealed class TestTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
