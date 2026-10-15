using System.Globalization;
using NetPilot.Abstractions;
using NetPilot.Core.Devices;

namespace NetPilot.Core.Rules.Presentation;

public enum RuleTriggerKind { UsageOver, HouseholdUsageOver, TimeWindow, DeviceJoined, DeviceIdle }
public enum RuleActionKind { Notify, Throttle, Block, SetWireless }

// Text fields retain invalid input so validation can explain every error without binding exceptions.
public sealed class RuleForm
{
    public Rule Original { get; set; } = new();
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public bool DryRun { get; set; } = true;
    public RuleScopeKind Scope { get; set; }
    public string Category { get; set; } = "";
    public HashSet<string> Devices { get; set; } = [];
    public RuleTriggerKind Trigger { get; set; } = RuleTriggerKind.DeviceJoined;
    public RulePeriod Period { get; set; }
    public string Gigabytes { get; set; } = "";
    public string Start { get; set; } = "22:00";
    public string End { get; set; } = "07:00";
    public HashSet<DayOfWeek> Days { get; set; } = Enum.GetValues<DayOfWeek>().ToHashSet();
    public string IdleDays { get; set; } = "14";
    public List<RuleActionForm> Actions { get; set; } = [];
    public RuleRevertKind Revert { get; set; }
    public string RevertMinutes { get; set; } = "60";
    public string CooldownMinutes { get; set; } = "60";
    public RuleOncePer OncePer { get; set; }

    public static IReadOnlyList<string> SafeNetworkIds { get; } = Array.AsReadOnly(
        (from kind in new[] { WirelessNetworkKind.Guest, WirelessNetworkKind.Iot }
         from band in Enum.GetValues<WirelessBand>() select new WirelessNetworkId(kind, band).ToString()).ToArray());

    public static string FormatGb(long bytes) => (bytes / 1_000_000_000m).ToString("0.#########", CultureInfo.InvariantCulture);

    public static bool TryBytes(string text, out long bytes)
    {
        bytes = 0;
        if (!decimal.TryParse(text, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite,
            CultureInfo.InvariantCulture, out var gb) || gb <= 0 || gb > long.MaxValue / 1_000_000_000m) return false;
        var value = gb * 1_000_000_000m;
        if (value != decimal.Truncate(value)) return false;
        bytes = (long)value;
        return true;
    }

    public Rule? Build(out IReadOnlyList<string> errors)
    {
        List<string> problems = [];
        long bytes = 0;
        if (Trigger is RuleTriggerKind.UsageOver or RuleTriggerKind.HouseholdUsageOver && !TryBytes(Gigabytes, out bytes))
            problems.Add("Enter a positive usage amount in decimal GB using a decimal point (1 GB = 1,000,000,000 bytes; at most 9 decimal places, within the supported range).");
        List<MacAddress> macs = [];
        if (Scope == RuleScopeKind.Devices)
            foreach (var text in Devices)
            {
                try { macs.Add(new MacAddress(text)); }
                catch (ArgumentException) { problems.Add($"Choose a valid device MAC address: {text}."); }
            }
        RuleTrigger trigger = Trigger switch
        {
            RuleTriggerKind.UsageOver => new UsageOver(Period, bytes),
            RuleTriggerKind.HouseholdUsageOver => new HouseholdUsageOver(Period, bytes),
            RuleTriggerKind.TimeWindow => new TimeWindow(Start, End, Days.Order().ToArray()),
            RuleTriggerKind.DeviceIdle => new DeviceIdle(Integer(IdleDays, "Idle days", problems)),
            _ => new DeviceJoined()
        };
        var rule = Original with
        {
            Name = Name.Trim(), Enabled = Enabled, DryRun = DryRun,
            Scope = new() { Kind = Scope, CategoryKey = Scope == RuleScopeKind.Category ? Category : null, Devices = macs.ToArray() },
            Trigger = trigger, Actions = Actions.Select(a => a.Build(problems)).ToArray(),
            Revert = new(Revert, Revert == RuleRevertKind.AfterMinutes ? Integer(RevertMinutes, "Revert delay", problems) : 0),
            CooldownMinutes = Integer(CooldownMinutes, "Cooldown", problems), OncePer = OncePer
        };
        problems.AddRange(rule.Validate().Select(PlainValidationError));
        if (!Enum.IsDefined(Trigger)) problems.Add("Choose a supported trigger.");
        errors = problems.Distinct().ToArray();
        return errors.Count == 0 ? rule : null;
    }

    private static string PlainValidationError(string error) => error switch
    {
        "UsageOver requires a valid period and bytes greater than zero." => "Device usage needs a Day or Month period and an amount greater than zero.",
        "HouseholdUsageOver requires a valid period and bytes greater than zero." => "Household usage needs a Day or Month period and an amount greater than zero.",
        "TimeWindow requires distinct start and end times in HH:mm format." => "Choose different start and end times in HH:mm format.",
        "TimeWindow must select at least one valid weekday." => "Select at least one weekday for the time window.",
        "WhenTriggerEnds requires a TimeWindow trigger." => "Releasing when the time window ends requires a time window trigger.",
        "SetWireless requires a valid guest-* or iot-* network; main networks are forbidden." => "Choose a valid guest-* or iot-* network; main networks are forbidden.",
        _ => error
    };

    internal static int Integer(string value, string label, List<string> errors)
    {
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)) return result;
        errors.Add($"{label} must be a whole number within the supported range.");
        return -1;
    }

    public static RuleForm From(Rule rule)
    {
        var form = new RuleForm
        {
            Original = rule, Name = rule.Name, Enabled = rule.Enabled, DryRun = rule.DryRun, Scope = rule.Scope.Kind,
            Category = rule.Scope.CategoryKey ?? "", Devices = rule.Scope.Devices.Select(m => m.ToString()).ToHashSet(),
            Actions = rule.Actions.Select(RuleActionForm.From).ToList(), Revert = rule.Revert.Kind,
            RevertMinutes = rule.Revert.Minutes.ToString(CultureInfo.InvariantCulture), OncePer = rule.OncePer,
            CooldownMinutes = rule.CooldownMinutes.ToString(CultureInfo.InvariantCulture)
        };
        switch (rule.Trigger)
        {
            case UsageOver u: form.Trigger = RuleTriggerKind.UsageOver; form.Period = u.Period; form.Gigabytes = FormatGb(u.Bytes); break;
            case HouseholdUsageOver h: form.Trigger = RuleTriggerKind.HouseholdUsageOver; form.Period = h.Period; form.Gigabytes = FormatGb(h.Bytes); break;
            case TimeWindow w: form.Trigger = RuleTriggerKind.TimeWindow; form.Start = w.Start; form.End = w.End; form.Days = w.Days.ToHashSet(); break;
            case DeviceIdle i: form.Trigger = RuleTriggerKind.DeviceIdle; form.IdleDays = i.Days.ToString(CultureInfo.InvariantCulture); break;
        }
        return form;
    }
}

public sealed class RuleActionForm
{
    public RuleActionKind Kind { get; set; }
    public string Title { get; set; } = "{rule}";
    public string Body { get; set; } = "{device}: {used} of {cap} bytes";
    public string Kbps { get; set; } = "256";
    public string NetworkId { get; set; } = "";
    public bool WirelessEnabled { get; set; }

    internal RuleAction Build(List<string> errors)
    {
        if (!Enum.IsDefined(Kind)) errors.Add("Choose a supported action.");
        return Kind switch
        {
            RuleActionKind.Throttle => new Throttle(RuleForm.Integer(Kbps, "Throttle Kbps", errors)),
            RuleActionKind.Block => new Block(),
            RuleActionKind.SetWireless => new SetWireless(NetworkId, WirelessEnabled),
            _ => new Notify(Title, Body)
        };
    }

    public static RuleActionForm From(RuleAction action) => action switch
    {
        Notify n => new() { Title = n.Title, Body = n.Body },
        Throttle t => new() { Kind = RuleActionKind.Throttle, Kbps = t.Kbps.ToString(CultureInfo.InvariantCulture) },
        Block => new() { Kind = RuleActionKind.Block },
        SetWireless w => new() { Kind = RuleActionKind.SetWireless, NetworkId = w.NetworkId, WirelessEnabled = w.Enabled },
        _ => throw new ArgumentException("Unsupported saved action.", nameof(action))
    };
}
