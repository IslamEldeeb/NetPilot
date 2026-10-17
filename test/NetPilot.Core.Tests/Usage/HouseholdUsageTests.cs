using NetPilot.Core.Devices;
using NetPilot.Core.Usage;

namespace NetPilot.Core.Tests.Usage;

public class HouseholdUsageTests
{
    private static readonly MacAddress Known = new("AA-BB-CC-DD-EE-01"), Removed = new("AA-BB-CC-DD-EE-02");
    private static readonly DateTimeOffset Finalized = DateTimeOffset.Parse("2026-10-16T12:00:00Z");
    private static UsageReadings Readings => new([
        new() { Mac = Known, FriendlyName = "TV", CategoryKey = "television" }
    ], [new() { Mac = Known, CurrentDayKey = "2026-10-16", CurrentDayBytes = 20, CurrentDayLanBytes = 40,
        CurrentMonthKey = "2026-10", CurrentMonthBytes = 200, CurrentMonthLanBytes = 400 }], [
        new(Known, "2026-10-15", 10, Finalized, 30), new(Removed, "2026-10-15", 5, Finalized, 5),
        new(Known, "2026-10-16", 999, Finalized), new(Known, "2026-10-01", 50, Finalized)
    ], [new(Removed, "2026-09", 300, Finalized, 600)], "2026-10-16");

    [Fact]
    public void Week_SumsDailyAndCurrentOnce_IncludesMissingDeviceAndHonestGaps()
    {
        var range = UsageRange.For(ReportPeriod.Week, new(2026, 10, 16));
        Assert.Equal(new(2026, 10, 12), range.Start);
        Assert.Equal(new(2026, 10, 19), range.EndExclusive);
        var report = HouseholdUsage.Report(Readings, range);
        Assert.Equal(35, report.InternetBytes);
        Assert.Equal(75, report.LanBytes);
        Assert.Equal(2, report.ObservedDays);
        Assert.Equal(30, report.Rows.Single(row => row.Mac == Known.ToString()).InternetBytes);
        Assert.Equal(DeviceCategory.UnknownKey, report.Rows.Single(row => row.Mac == Removed.ToString()).Category);
        Assert.Null(HouseholdUsage.Days(Readings, range)[0].InternetBytes);
    }

    [Fact]
    public void MonthlyLegacyCounters_RemainAuthoritative_WhileBillingRangeUsesDailyRows()
    {
        Assert.Equal(200, HouseholdUsage.Report(Readings, UsageRange.For(ReportPeriod.Month, new(2026, 10, 16))).InternetBytes);
        Assert.Equal(300, HouseholdUsage.Report(Readings, UsageRange.For(ReportPeriod.Month, new(2026, 9, 16))).InternetBytes);
        Assert.Equal(35, HouseholdUsage.Report(Readings, new(new(2026, 10, 10), new(2026, 11, 10))).InternetBytes);
    }

    [Fact]
    public void DeviceFilter_AppliesToBothTotalsAndRows_EmptyCurrentDayIsUnknown()
    {
        var range = UsageRange.For(ReportPeriod.Day, new(2026, 10, 15));
        var report = HouseholdUsage.Report(Readings, range, Removed);
        Assert.Equal(5, report.InternetBytes);
        Assert.Equal(5, report.LanBytes);
        Assert.Single(report.Rows);
        Assert.False(HouseholdUsage.Report(Readings, UsageRange.For(ReportPeriod.Day, new(2026, 10, 17))).HasObservation);
    }

    [Fact]
    public void Csv_HasNeutralColumns_ExactBytes_DecimalGb_AndEscapesNamesAndFormulas()
    {
        var report = new UsageReport(new(new(2026, 10, 16), new(2026, 10, 17)),
            [new(Known, " =HYPERLINK(\"x\")\nTV", "+unsafe", 1_234_567_890, 2_000_000_000)], 1);
        var csv = HouseholdUsage.Csv(report, "Africa/Cairo", "Estimated");
        Assert.StartsWith("period_start,period_end_exclusive,timezone,mac,name,category,internet_bytes", csv);
        Assert.Contains("\"' =HYPERLINK(\"\"x\"\")\nTV\"", csv);
        Assert.Contains("\"'+unsafe\"", csv);
        Assert.Contains("\"1234567890\",\"2000000000\",\"1.23456789\",\"2\"", csv);
        Assert.Contains("\"2026-10-16\",\"2026-10-17\",\"Africa/Cairo\"", csv);
    }
}
