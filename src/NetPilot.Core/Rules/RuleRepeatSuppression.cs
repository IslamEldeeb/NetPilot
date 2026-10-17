using NetPilot.Core.Usage;

namespace NetPilot.Core.Rules;

internal static class RuleRepeatSuppression
{
    public static bool IsSuppressed(Rule rule, TriggerMatch match, RuleFiring? latest, UsagePeriodClock clock)
    {
        if (latest is null) return false;
        if (rule.Trigger is DeviceJoined) return true;
        if (rule.Trigger is TimeWindow or DeviceIdle && latest.TriggerEpisodeKey == match.EpisodeKey) return true;
        if (rule.OncePer == RuleOncePer.Day) return latest.DayKey == clock.TodayKey();
        if (rule.OncePer == RuleOncePer.Month) return latest.MonthKey == clock.ThisMonthKey();
        return latest.FiredAtUtc >= clock.GetUtcNow()
            || (clock.GetUtcNow() - latest.FiredAtUtc).TotalMinutes < rule.CooldownMinutes;
    }
}
