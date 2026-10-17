using NetPilot.Core.Devices;
using NetPilot.Core.Usage;

namespace NetPilot.Core.Tests.Devices;

public class DeviceUsageHistoryTests
{
    [Fact]
    public void DailyChart_UsesCurrentCounterAndHistory_LeavingMissingDaysAsGaps()
    {
        var mac = new MacAddress("AA-BB-CC-DD-EE-01");
        var state = new DeviceUsageState { Mac = mac, CurrentDayKey = "2026-10-17", CurrentDayBytes = 12, CurrentDayLanBytes = 4 };
        var days = DeviceUsageHistory.Last30Days(mac, "2026-10-17", state,
            [new(mac, "2026-10-16", 10, DateTimeOffset.UtcNow, 3),
             new(new("AA-BB-CC-DD-EE-02"), "2026-10-15", 99, DateTimeOffset.UtcNow)]);
        Assert.Equal(30, days.Count);
        Assert.Equal("2026-09-18", days[0].DayKey);
        Assert.Null(days[0].InternetBytes);
        Assert.Null(days[^3].InternetBytes);
        Assert.Equal(new DeviceUsageDay("2026-10-16", 10, 3), days[^2]);
        Assert.Equal(new DeviceUsageDay("2026-10-17", 12, 4), days[^1]);
        state.CurrentDayKey = "2026-10-16";
        Assert.Null(DeviceUsageHistory.Last30Days(mac, "2026-10-17", state, [])[29].InternetBytes);
    }
}
