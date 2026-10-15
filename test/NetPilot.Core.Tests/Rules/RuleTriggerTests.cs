using NetPilot.Core.Rules;
using static NetPilot.Core.Tests.Rules.RuleTestHarness;

namespace NetPilot.Core.Tests.Rules;

public class RuleTriggerTests
{
    [Theory]
    [InlineData(RulePeriod.Day, false, 1000, true)]
    [InlineData(RulePeriod.Month, false, 5000, true)]
    [InlineData(RulePeriod.Day, false, 1001, false)]
    [InlineData(RulePeriod.Month, false, 5001, false)]
    [InlineData(RulePeriod.Day, true, 1, false)]
    [InlineData(RulePeriod.Month, true, 1, false)]
    public async Task UsageOver_UsesMatchingInternetPeriodOnly(RulePeriod period, bool stale, long cap, bool fires)
    {
        var h = await CreateAsync();
        var state = (await h.Usage.FindStateAsync(h.Device.Mac, Ct))!;
        state.CurrentDayLanBytes = state.CurrentMonthLanBytes = 900000;
        if (stale) { state.CurrentDayKey = "2020-01-01"; state.CurrentMonthKey = "2020-01"; }
        await h.AddRuleAsync(h.Rule(new UsageOver(period, cap)));
        await h.TickAsync();
        Assert.Equal(fires ? 1 : 0, h.Firings.Items.Count);
        if (fires) Assert.Equal(period == RulePeriod.Day ? 1000 : 5000, h.OnlyFiring.UsedBytes);
    }

    [Theory]
    [InlineData(RulePeriod.Day)]
    [InlineData(RulePeriod.Month)]
    public async Task Household_SumsAllDevicesRegardlessOfScope_AndIgnoresStaleStates(RulePeriod period)
    {
        var h = await CreateAsync();
        var tv = await h.AddDeviceAsync(2, "tv");
        await h.SetUsageAsync(tv, 300, 300);
        var stale = await h.AddDeviceAsync(3);
        await h.SetUsageAsync(stale, 99999, 99999);
        var state = (await h.Usage.FindStateAsync(stale.Mac, Ct))!;
        state.CurrentDayKey = "old"; state.CurrentMonthKey = "old";
        var cap = period == RulePeriod.Day ? 1300 : 5300;
        await h.AddRuleAsync(h.Rule(new HouseholdUsageOver(period, cap), new Throttle(128)) with
        { Scope = new() { Kind = RuleScopeKind.Category, CategoryKey = "tv" } });
        await h.TickAsync();
        Assert.Equal("household", h.OnlyFiring.Subject);
        Assert.Equal(cap, h.OnlyFiring.UsedBytes);
        Assert.Equal(tv.Mac.ToString(), Assert.Single(h.Provider.AppliedLimits).Mac);
        Assert.Equal(tv.Mac, Assert.Single(await h.Claims.GetAllAsync(Ct)).Mac);
    }

    [Theory]
    [InlineData(RuleScopeKind.All, 2)]
    [InlineData(RuleScopeKind.Category, 1)]
    [InlineData(RuleScopeKind.Devices, 1)]
    public async Task Scope_ResolvesPersistedCategoryAndMacList(RuleScopeKind kind, int count)
    {
        var h = await CreateAsync();
        var tv = await h.AddDeviceAsync(2, "tv");
        await h.SetUsageAsync(tv, 2000, 5000);
        await h.AddRuleAsync(h.Rule() with { Scope = new() { Kind = kind, CategoryKey = "tv", Devices = [tv.Mac] } });
        await h.TickAsync();
        Assert.Equal(count, h.Firings.Items.Count);
        if (kind != RuleScopeKind.All) Assert.Equal(tv.Mac.ToString(), h.OnlyFiring.Subject);
    }

    [Theory]
    [InlineData("2026-07-25T12:00:00Z", "15:00", "16:00", DayOfWeek.Saturday, true)]
    [InlineData("2026-07-25T13:00:00Z", "15:00", "16:00", DayOfWeek.Saturday, false)]
    [InlineData("2026-07-25T12:00:00Z", "15:00", "16:00", DayOfWeek.Sunday, false)]
    [InlineData("2026-07-25T20:00:00Z", "22:00", "07:00", DayOfWeek.Saturday, true)]
    [InlineData("2026-07-26T02:00:00Z", "22:00", "07:00", DayOfWeek.Saturday, true)]
    [InlineData("2026-07-26T02:00:00Z", "22:00", "07:00", DayOfWeek.Sunday, false)]
    [InlineData("2026-07-26T04:00:00Z", "22:00", "07:00", DayOfWeek.Saturday, false)]
    [InlineData("2026-01-17T13:00:00Z", "15:00", "16:00", DayOfWeek.Saturday, true)]
    [InlineData("2026-04-23T22:00:00Z", "01:00", "02:00", DayOfWeek.Friday, true)]
    [InlineData("2026-04-23T22:00:00Z", "00:00", "01:00", DayOfWeek.Friday, false)]
    public async Task TimeWindow_UsesCairoDstHalfOpenBoundariesAndWindowStartWeekday(
        string utc, string start, string end, DayOfWeek day, bool fires)
    {
        var h = await CreateAsync();
        h.Time.UtcNow = DateTimeOffset.Parse(utc);
        await h.AddRuleAsync(h.Rule(new TimeWindow(start, end, [day])));
        await h.TickAsync();
        Assert.Equal(fires ? 1 : 0, h.Firings.Items.Count);
        if (fires && start == "22:00") Assert.Equal("2026-07-25", h.OnlyFiring.EpisodeKey);
    }

    [Fact]
    public async Task TimeWindow_RepeatedDstHourAndRestart_DoNotRefireSameOccurrence()
    {
        var h = await CreateAsync();
        h.Time.UtcNow = DateTimeOffset.Parse("2026-10-29T20:30:00Z");
        await h.AddRuleAsync(h.Rule(new TimeWindow("23:00", "23:59", [DayOfWeek.Thursday])) with { CooldownMinutes = 0 });
        await h.TickAsync();
        h.Time.UtcNow = h.Time.UtcNow.AddHours(1);
        await h.TickAsync();
        Assert.Single(h.Channel.Messages);
        Assert.Equal("2026-10-29", h.OnlyFiring.EpisodeKey);
    }

    [Fact]
    public async Task DeviceJoined_IsNotRetroactive_AndFiresOnceAfterCreationEvenAfterPrune()
    {
        var h = await CreateAsync();
        await h.AddRuleAsync(h.Rule(new DeviceJoined()) with { CreatedAtUtc = h.Time.UtcNow, CooldownMinutes = 0 });
        await h.TickAsync();
        Assert.Empty(h.Channel.Messages);
        var joined = await h.AddDeviceAsync(2);
        joined = WithFirstSeen(joined, h.Time.UtcNow);
        await h.Devices.UpsertAsync(joined, Ct);
        await h.TickAsync();
        await h.Firings.PruneAsync(0, Ct);
        h.Time.UtcNow = h.Time.UtcNow.AddDays(1);
        await h.TickAsync();
        Assert.Single(h.Channel.Messages);

        static NetPilot.Core.Devices.Device WithFirstSeen(NetPilot.Core.Devices.Device device, DateTimeOffset time) => new()
        { Mac = device.Mac, Hostname = device.Hostname, FirstSeenAtUtc = time, LastSeenAtUtc = time, CategoryKey = device.CategoryKey };
    }

    [Fact]
    public async Task DeviceIdle_FiresOncePerLastSeenEpisode()
    {
        var h = await CreateAsync();
        await h.AddRuleAsync(h.Rule(new DeviceIdle(7)) with { CooldownMinutes = 0 });
        h.Device.LastSeenAtUtc = h.Time.UtcNow.AddDays(-7).AddSeconds(1);
        await h.TickAsync();
        Assert.Empty(h.Channel.Messages);
        h.Time.UtcNow = h.Time.UtcNow.AddSeconds(1);
        await h.TickAsync();
        h.Time.UtcNow = h.Time.UtcNow.AddDays(1);
        await h.TickAsync();
        Assert.Single(h.Channel.Messages);
        h.Device.LastSeenAtUtc = h.Time.UtcNow;
        await h.TickAsync();
        h.Time.UtcNow = h.Time.UtcNow.AddDays(7);
        await h.TickAsync();
        Assert.Equal(2, h.Channel.Messages.Count);
    }
}
