using System.Globalization;

namespace NetPilot.Core.Usage;

/// <summary>
/// Keeps usage periods aligned with the configured household timezone, including DST,
/// while persisted timestamps remain UTC instants.
/// </summary>
public class UsagePeriodClock(string timeZoneId = UsagePeriodClock.DefaultTimeZoneId, TimeProvider? timeProvider = null)
{
    public const string DefaultTimeZoneId = "Africa/Cairo";

    public TimeZoneInfo TimeZone { get; } = ResolveTimeZone(timeZoneId);

    public DateTimeOffset GetUtcNow() => (timeProvider ?? TimeProvider.System).GetUtcNow();

    public string DayKey(DateTimeOffset instant) =>
        TimeZoneInfo.ConvertTime(instant, TimeZone).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public string MonthKey(DateTimeOffset instant) =>
        TimeZoneInfo.ConvertTime(instant, TimeZone).ToString("yyyy-MM", CultureInfo.InvariantCulture);

    public string TodayKey() => DayKey(GetUtcNow());

    public string ThisMonthKey() => MonthKey(GetUtcNow());

    private static TimeZoneInfo ResolveTimeZone(string timeZoneId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
        {
            throw new ArgumentException(
                $"Invalid NetPilot:TimeZone '{timeZoneId}'. Configure a valid IANA timezone ID (for example, '{DefaultTimeZoneId}') and ensure tzdata is installed.",
                nameof(timeZoneId), ex);
        }
    }
}
