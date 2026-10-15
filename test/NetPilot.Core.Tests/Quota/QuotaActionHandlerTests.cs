using NetPilot.Core.Devices;
using NetPilot.Core.Notifications;
using NetPilot.Core.Quota;
using NetPilot.Core.Tests.Fakes;
using NetPilot.Core.Usage;

namespace NetPilot.Core.Tests.Quota;

public class QuotaActionHandlerTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private readonly UsagePeriodClock _clock = new();
    private readonly InMemoryQuotaStateStore _states = new();
    private readonly InMemoryDeviceStore _devices = new();
    private readonly InMemoryActivityLogStore _log = new();
    private readonly Tokens _tokens = new();
    private QuotaActionHandler Handler => new(_tokens, _states, _devices, _log, _clock);

    [Fact]
    public async Task ExtendAddsExactlyOneDecimalGbOnce_OnlyToNamedPeriod()
    {
        var day = await SeedAsync();
        var month = day with { PeriodKind = QuotaPeriodKind.Month, PeriodKey = _clock.ThisMonthKey(), ExtraAllowanceBytes = 70 };
        await _states.UpsertAsync(month, Ct);
        var token = _tokens.Add(day, NotificationActionIds.Extend1Gb);
        var accepted = await Handler.ApplyAsync(token);
        Assert.Equal(QuotaActionStatus.Ok, accepted.Status);
        Assert.Contains("next tick", accepted.Message);
        Assert.Equal(1_000_000_020, (await _states.FindAsync(day.Id, Ct))!.ExtraAllowanceBytes);
        Assert.Equal(70, (await _states.FindAsync(month.Id, Ct))!.ExtraAllowanceBytes);
        Assert.Equal(QuotaActionStatus.AlreadyUsed, (await Handler.ApplyAsync(token)).Status);
        Assert.Equal(1_000_000_020, (await _states.FindAsync(day.Id, Ct))!.ExtraAllowanceBytes);
        Assert.Single(_log.Entries);
        Assert.DoesNotContain(token, _log.Entries[0].Message);
    }

    [Fact]
    public async Task RestoreSetsRequestWithoutClearingActiveAction_AndRejectsActionMismatch()
    {
        var day = await SeedAsync();
        var token = _tokens.Add(day, NotificationActionIds.RestoreNow);
        Assert.Equal(QuotaActionStatus.Invalid, (await Handler.ApplyAsync(token, NotificationActionIds.Extend1Gb)).Status);
        Assert.False((await _states.FindAsync(day.Id, Ct))!.ReleaseRequested);
        Assert.Equal(QuotaActionStatus.Ok, (await Handler.ApplyAsync(token, NotificationActionIds.RestoreNow)).Status);
        var updated = (await _states.FindAsync(day.Id, Ct))!;
        Assert.True(updated.ReleaseRequested);
        Assert.Equal(QuotaActionKind.Throttle, updated.ActionKind);
        Assert.False(updated.ActionSuppressedForPeriod);
        Assert.Single(updated.UsedActionTokenHashes);
        Assert.Equal(QuotaActionStatus.AlreadyUsed, (await Handler.ApplyAsync(token)).Status);
    }

    [Theory]
    [InlineData("device", QuotaActionStatus.NotFound)]
    [InlineData("period", QuotaActionStatus.NotFound)]
    [InlineData("old-period", QuotaActionStatus.Expired)]
    [InlineData("invalid", QuotaActionStatus.Invalid)]
    [InlineData("expired", QuotaActionStatus.Expired)]
    public async Task MissingOrInvalidTargetsMakeNoChanges(string scenario, QuotaActionStatus status)
    {
        var day = await SeedAsync();
        var target = scenario switch
        {
            "device" => day with { Mac = new("AA-BB-CC-DD-EE-02") },
            "period" => day with { PeriodKind = QuotaPeriodKind.Month, PeriodKey = _clock.ThisMonthKey() },
            "old-period" => day with { PeriodKey = "2000-01-01" },
            _ => day
        };
        if (scenario == "old-period") await _states.UpsertAsync(target, Ct);
        var token = scenario is "invalid" or "expired" ? scenario : _tokens.Add(target, NotificationActionIds.Extend1Gb);
        Assert.Equal(status, (await Handler.ApplyAsync(token)).Status);
        Assert.Equal(20, (await _states.FindAsync(day.Id, Ct))!.ExtraAllowanceBytes);
        Assert.Empty(_log.Entries);
    }

    private async Task<QuotaPeriodState> SeedAsync()
    {
        var state = new QuotaPeriodState { Mac = new("AA-BB-CC-DD-EE-01"), PeriodKind = QuotaPeriodKind.Day,
            PeriodKey = _clock.TodayKey(), ExtraAllowanceBytes = 20, ActionKind = QuotaActionKind.Throttle };
        await _devices.UpsertAsync(new Device { Mac = state.Mac }, Ct);
        await _states.UpsertAsync(state, Ct);
        return state;
    }

    private sealed class Tokens : IQuotaActionTokenService
    {
        private readonly Dictionary<string, QuotaActionClaim> _claims = [];
        public string Create(QuotaActionClaim claim)
        {
            var token = Guid.NewGuid().ToString();
            _claims[token] = claim;
            return token;
        }
        public string Add(QuotaPeriodState state, string action) => Create(new(state.Mac, state.PeriodKind, state.PeriodKey, action));
        public QuotaActionTokenResult Validate(string? token) => token is not null && _claims.TryGetValue(token, out var claim)
            ? new(QuotaActionStatus.Ok, claim) : new(token == "expired" ? QuotaActionStatus.Expired : QuotaActionStatus.Invalid);
    }
}
