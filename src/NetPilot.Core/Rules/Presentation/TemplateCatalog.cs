namespace NetPilot.Core.Rules.Presentation;

public sealed record RuleTemplate(string Id, string Name, string Description, string Hint, Func<Rule> Create);

public static class TemplateCatalog
{
    public static IReadOnlyList<RuleTemplate> All { get; } = Array.AsReadOnly<RuleTemplate>([
        new("bedtime", "Bedtime throttle", "Throttle selected devices to 256 Kbps from 22:00 to 07:00, then restore.",
            "Choose the devices or category; protect your management device in Quotas.", () => new()
            {
                Name = "Bedtime throttle", Scope = new() { Kind = RuleScopeKind.Devices },
                Trigger = Night("22:00", "07:00"), Actions = [new Throttle(256)], Revert = new(RuleRevertKind.WhenTriggerEnds)
            }),
        new("binge", "Binge alert", "Notify when a device uses over 10 GB of Internet data today, once per day.",
            "Choose a category, such as Television or Game Console; this only notifies.", () => new()
            {
                Name = "Binge alert", Scope = new() { Kind = RuleScopeKind.Category }, Trigger = new UsageOver(RulePeriod.Day, 10_000_000_000),
                Actions = [new Notify("{rule}", "{device} used {used} bytes today (threshold {cap}).")], OncePer = RuleOncePer.Day
            }),
        new("new-device", "New device guard", "Notify and throttle newly discovered devices to 256 Kbps until you release them from history.",
            "Release now is the approval step. Existing devices are not treated as newly joined; protected devices are exempt.", () => new()
            {
                Name = "New device guard", Trigger = new DeviceJoined(),
                Actions = [new Notify("New device: {device}", "Review {device} in Rules and use Release now to approve."), new Throttle(256)]
            }),
        new("household-cap", "Household monthly ISP cap warning", "Notify once per month when household Internet usage exceeds your chosen warning threshold.",
            "Enter about 80% of your real ISP cap in GB; for a 250 GB plan, enter 200 GB.", () => new()
            {
                Name = "Household monthly ISP cap warning", Trigger = new HouseholdUsageOver(RulePeriod.Month, 0),
                Actions = [new Notify("{rule}", "Household used {used} bytes this month (warning threshold {cap}).")], OncePer = RuleOncePer.Month
            }),
        new("idle", "Idle device cleanup reminder", "Notify when a device has not been seen for 14 days, with a seven-day cooldown.",
            "Reminder only; no device is deleted. One reminder per idle episode until the device is seen again.", () => new()
            {
                Name = "Idle device cleanup reminder", Trigger = new DeviceIdle(14), CooldownMinutes = 10080,
                Actions = [new Notify("{rule}", "{device} has been idle for at least 14 days; review it for cleanup.")], OncePer = RuleOncePer.None
            }),
        new("guest-night", "Guest WiFi off at night", "Turn a guest network off from 01:00 to 06:00 and restore its prior enabled state afterwards.",
            "Pick the guest network id in the action. Main WiFi is forbidden; restoration preserves a differing manual enabled state.", () => new()
            {
                Name = "Guest WiFi off at night", Trigger = Night("01:00", "06:00"),
                Actions = [new SetWireless("", false)], Revert = new(RuleRevertKind.WhenTriggerEnds)
            })
    ]);

    private static TimeWindow Night(string start, string end) => new(start, end, Enum.GetValues<DayOfWeek>());
}
