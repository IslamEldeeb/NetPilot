using System.Globalization;
using System.Text;
using NetPilot.Core.Devices;

namespace NetPilot.Core.Usage;

public enum ReportPeriod { Day, Week, Month }
public sealed record UsageRange(DateOnly Start, DateOnly EndExclusive)
{
    public static UsageRange For(ReportPeriod period, DateOnly date) => period switch
    {
        ReportPeriod.Day => new(date, date.AddDays(1)),
        ReportPeriod.Week => new(date.AddDays(-((int)date.DayOfWeek + 6) % 7), date.AddDays(7 - ((int)date.DayOfWeek + 6) % 7)),
        ReportPeriod.Month => new(new(date.Year, date.Month, 1), new DateOnly(date.Year, date.Month, 1).AddMonths(1)),
        _ => throw new ArgumentOutOfRangeException(nameof(period))
    };
    public bool Contains(string day) => string.CompareOrdinal(day, Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) >= 0
        && string.CompareOrdinal(day, EndExclusive.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) < 0;
}
public sealed record UsageRow(string Mac, string Name, string Category, long InternetBytes, long LanBytes);
public sealed record UsageReport(UsageRange Range, IReadOnlyList<UsageRow> Rows, int ObservedDays)
{
    public long InternetBytes => Rows.Sum(row => row.InternetBytes);
    public long LanBytes => Rows.Sum(row => row.LanBytes);
    public bool HasObservation => ObservedDays > 0;
}
public sealed record UsageReadings(IReadOnlyList<Device> Devices, IReadOnlyList<DeviceUsageState> States,
    IReadOnlyList<UsageDailyHistoryEntry> Daily, IReadOnlyList<UsageHistoryEntry> Monthly, string Today);

public static class HouseholdUsage
{
    public static UsageReport Report(UsageReadings readings, UsageRange range, string? mac = null)
    {
        var wholeMonth = range.Start.Day == 1 && range.EndExclusive == range.Start.AddMonths(1);
        var key = range.Start.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        // Monthly counters remain authoritative, retaining old history even when daily coverage is absent.
        var totals = wholeMonth ? MonthlyTotals(readings, key) : DailySamples(readings).Where(day => range.Contains(day.DayKey))
            .GroupBy(day => day.Mac).ToDictionary(group => group.Key,
                group => (Internet: group.Sum(day => day.Internet), Lan: group.Sum(day => day.Lan)));
        var identities = readings.Devices.ToDictionary(device => device.Mac.ToString());
        var rows = totals.Keys.Where(address => string.IsNullOrEmpty(mac) || address == mac).Select(address =>
        {
            identities.TryGetValue(address, out var device);
            var name = device is null ? address : DeviceName(device);
            return new UsageRow(address, name, device?.CategoryKey ?? DeviceCategory.UnknownKey,
                totals[address].Internet, totals[address].Lan);
        }).OrderByDescending(row => row.InternetBytes).ThenBy(row => row.Mac).ToArray();
        var observed = Days(readings, range, mac).Count(day => day.InternetBytes is not null);
        if (wholeMonth && rows.Length > 0) observed = Math.Max(1, observed);
        return new(range, rows, observed);
    }

    private sealed record DailySample(string Mac, string DayKey, long Internet, long Lan);
    private static IEnumerable<DailySample> DailySamples(UsageReadings readings) => readings.Daily
        .Where(day => day.DayKey != readings.Today).Select(day => new DailySample(day.Mac, day.DayKey, day.TotalBytes, day.LanBytes))
        .Concat(readings.States.Where(state => state.CurrentDayKey == readings.Today)
            .Select(state => new DailySample(state.Mac, state.CurrentDayKey, state.CurrentDayBytes, state.CurrentDayLanBytes)));

    private static Dictionary<string, (long Internet, long Lan)> MonthlyTotals(UsageReadings readings, string key)
    {
        var current = key == readings.Today[..7];
        var internet = UsageQuery.BytesByDevice(UsagePeriodType.Month, key, current, readings.States, readings.Monthly, readings.Daily);
        var lan = UsageQuery.LanBytesByDevice(UsagePeriodType.Month, key, current, readings.States, readings.Monthly, readings.Daily);
        return internet.Keys.Union(lan.Keys).ToDictionary(mac => mac,
            mac => (internet.GetValueOrDefault(mac), lan.GetValueOrDefault(mac)));
    }

    public static IReadOnlyList<DeviceUsageDay> Days(UsageReadings readings, UsageRange range, string? mac = null)
    {
        var totals = DailySamples(readings).Where(day => range.Contains(day.DayKey)
                && (string.IsNullOrEmpty(mac) || day.Mac == mac))
            .GroupBy(day => day.DayKey).ToDictionary(group => group.Key,
                group => new DeviceUsageDay(group.Key, group.Sum(day => day.Internet), group.Sum(day => day.Lan)));
        return Enumerable.Range(0, range.EndExclusive.DayNumber - range.Start.DayNumber).Select(offset =>
        {
            var key = range.Start.AddDays(offset).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            return totals.GetValueOrDefault(key) ?? new(key, null, null);
        }).ToArray();
    }

    public static string DeviceName(Device device) => !string.IsNullOrWhiteSpace(device.FriendlyName)
        ? device.FriendlyName : !string.IsNullOrWhiteSpace(device.Hostname) ? device.Hostname : device.Mac.ToString();

    public static string Csv(UsageReport report, string timezone, string confidence)
    {
        var csv = new StringBuilder("period_start,period_end_exclusive,timezone,mac,name,category,internet_bytes,excluded_lan_bytes,internet_gb,excluded_lan_gb,confidence\r\n");
        foreach (var row in report.Rows)
            csv.Append(string.Join(',', new[] { report.Range.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), report.Range.EndExclusive.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                timezone, row.Mac, row.Name, row.Category, row.InternetBytes.ToString(CultureInfo.InvariantCulture),
                row.LanBytes.ToString(CultureInfo.InvariantCulture), DataSize.ToGb(row.InternetBytes).ToString(CultureInfo.InvariantCulture),
                DataSize.ToGb(row.LanBytes).ToString(CultureInfo.InvariantCulture), confidence }.Select(CsvCell))).Append("\r\n");
        return csv.ToString();
    }
    private static string CsvCell(string cell)
    {
        if (cell.TrimStart().FirstOrDefault() is '=' or '+' or '-' or '@' or '\t' or '\r' or '\n') cell = "'" + cell;
        return "\"" + cell.Replace("\"", "\"\"") + "\"";
    }
}
