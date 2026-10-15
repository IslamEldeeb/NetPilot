using System.Globalization;
using NetPilot.Core.Tests.Fakes;
using NetPilot.Core.Usage;

namespace NetPilot.Core.Tests.Usage;

public class UsagePeriodClockTests
{
    // Cairo midnight is 21:00Z in summer and 22:00Z in winter; UTC midnight stays in the same Cairo day.
    [Theory]
    [InlineData("2026-07-25T20:59:59Z", "2026-07-25")]
    [InlineData("2026-07-25T21:00:00Z", "2026-07-26")]
    [InlineData("2026-07-25T21:30:00Z", "2026-07-26")]
    [InlineData("2026-07-26T00:00:00Z", "2026-07-26")]
    [InlineData("2026-01-25T21:59:59Z", "2026-01-25")]
    [InlineData("2026-01-25T22:00:00Z", "2026-01-26")]
    [InlineData("2026-01-26T00:00:00Z", "2026-01-26")]
    public void DayKey_UsesCairoMidnight(string instant, string expected)
    {
        var clock = new UsagePeriodClock();

        Assert.Equal(expected, clock.DayKey(DateTimeOffset.Parse(instant, CultureInfo.InvariantCulture)));
    }

    [Theory]
    [InlineData("2026-07-31T20:59:59Z", "2026-07")]
    [InlineData("2026-07-31T21:00:00Z", "2026-08")]
    [InlineData("2026-01-31T21:59:59Z", "2026-01")]
    [InlineData("2026-01-31T22:00:00Z", "2026-02")]
    public void MonthKey_UsesCairoMonthBoundary(string instant, string expected)
    {
        var clock = new UsagePeriodClock();

        Assert.Equal(expected, clock.MonthKey(DateTimeOffset.Parse(instant, CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void CurrentKeys_UseInjectedTimeProvider()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 7, 31, 20, 59, 59, TimeSpan.Zero));
        var clock = new UsagePeriodClock(timeProvider: time);

        Assert.Equal("2026-07-31", clock.TodayKey());
        Assert.Equal("2026-07", clock.ThisMonthKey());

        time.UtcNow = time.UtcNow.AddSeconds(1);

        Assert.Equal("2026-08-01", clock.TodayKey());
        Assert.Equal("2026-08", clock.ThisMonthKey());
    }

    [Fact]
    public void Keys_UseConfiguredZone()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 7, 31, 21, 30, 0, TimeSpan.Zero));
        var clock = new UsagePeriodClock("Etc/UTC", time);

        Assert.Equal("2026-07-31", clock.TodayKey());
        Assert.Equal("2026-07", clock.ThisMonthKey());
    }

    [Theory]
    [InlineData("Not/A_TimeZone")]
    [InlineData("")]
    public void InvalidZone_IsRejectedWithConfigurationMessage(string zoneId)
    {
        var exception = Assert.Throws<ArgumentException>(() => new UsagePeriodClock(zoneId));

        Assert.Contains("NetPilot:TimeZone", exception.Message);
        Assert.Contains($"'{zoneId}'", exception.Message);
    }
}
