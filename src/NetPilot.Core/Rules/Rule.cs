using System.Globalization;
using System.Text.Json.Serialization;
using NetPilot.Abstractions;
using NetPilot.Core.Devices;

namespace NetPilot.Core.Rules;

public enum RuleScopeKind { All, Category, Devices }
public enum RulePeriod { Day, Month }
public enum RuleOncePer { None, Day, Month }
public enum RuleRevertKind { None, AtPeriodEnd, AfterMinutes, WhenTriggerEnds }

public record RuleScope
{
    public RuleScopeKind Kind { get; init; }
    public string? CategoryKey { get; init; }
    public MacAddress[] Devices { get; init; } = [];

    public bool Matches(Device device) => Kind switch
    {
        RuleScopeKind.All => true,
        RuleScopeKind.Category => device.CategoryKey == CategoryKey,
        RuleScopeKind.Devices => Devices.Contains(device.Mac),
        _ => false
    };
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(UsageOver), "usage-over")]
[JsonDerivedType(typeof(HouseholdUsageOver), "household-usage-over")]
[JsonDerivedType(typeof(TimeWindow), "time-window")]
[JsonDerivedType(typeof(DeviceJoined), "device-joined")]
[JsonDerivedType(typeof(DeviceIdle), "device-idle")]
public abstract record RuleTrigger;
public sealed record UsageOver(RulePeriod Period, long Bytes) : RuleTrigger;
public sealed record HouseholdUsageOver(RulePeriod Period, long Bytes) : RuleTrigger;
public sealed record TimeWindow(string Start, string End, DayOfWeek[] Days) : RuleTrigger;
public sealed record DeviceJoined : RuleTrigger;
public sealed record DeviceIdle(int Days) : RuleTrigger;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(Notify), "notify")]
[JsonDerivedType(typeof(Throttle), "throttle")]
[JsonDerivedType(typeof(Block), "block")]
[JsonDerivedType(typeof(SetWireless), "set-wireless")]
public abstract record RuleAction;
public sealed record Notify(string Title, string Body) : RuleAction;
public sealed record Throttle(int Kbps) : RuleAction;
public sealed record Block : RuleAction;
public sealed record SetWireless(string NetworkId, bool Enabled) : RuleAction;
public record RuleRevert(RuleRevertKind Kind = RuleRevertKind.None, int Minutes = 0);

public record Rule
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "";
    public bool Enabled { get; init; } = true;
    public bool DryRun { get; init; } = true;
    public RuleScope Scope { get; init; } = new();
    public RuleTrigger Trigger { get; init; } = new DeviceJoined();
    public RuleAction[] Actions { get; init; } = [];
    public RuleRevert Revert { get; init; } = new();
    public int CooldownMinutes { get; init; } = 60;
    public RuleOncePer OncePer { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public IReadOnlyList<string> Validate()
    {
        List<string> errors = [];
        if (Id == Guid.Empty) errors.Add("Rule ID is required.");
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 80) errors.Add("Name is required and must be at most 80 characters.");
        if (CooldownMinutes < 0) errors.Add("Cooldown must be zero or more minutes.");
        if (!Enum.IsDefined(OncePer)) errors.Add("Choose a valid once-per period.");
        if (Scope is null || !Enum.IsDefined(Scope.Kind)
            || (Scope.Kind == RuleScopeKind.Category && string.IsNullOrWhiteSpace(Scope.CategoryKey))
            || (Scope.Kind == RuleScopeKind.Devices && (Scope.Devices is null || Scope.Devices.Length == 0
                || Scope.Devices.Any(m => string.IsNullOrWhiteSpace(m.ToString())))))
            errors.Add("Scope must select all devices, a category, or a non-empty device list.");
        ValidateTrigger(errors);
        if (Revert is null || !Enum.IsDefined(Revert.Kind)) errors.Add("Choose a valid revert policy.");
        else
        {
            if (Revert.Kind == RuleRevertKind.AfterMinutes && Revert.Minutes <= 0) errors.Add("Revert delay must be greater than zero minutes.");
            if (Revert.Kind == RuleRevertKind.WhenTriggerEnds && Trigger is not TimeWindow) errors.Add("WhenTriggerEnds requires a TimeWindow trigger.");
        }
        if (Actions is null || Actions.Length == 0) errors.Add("At least one action is required.");
        else foreach (var action in Actions)
        {
            switch (action)
            {
                case Throttle t when t.Kbps <= 0: errors.Add("Throttle Kbps must be greater than zero."); break;
                case SetWireless w when !IsSafeWireless(w.NetworkId): errors.Add("SetWireless requires a valid guest-* or iot-* network; main networks are forbidden."); break;
                case Notify n when n.Title is null || n.Body is null: errors.Add("Notify title and body cannot be null."); break;
                case Notify or Throttle or Block or SetWireless: break;
                default: errors.Add("Choose a supported action."); break;
            }
        }
        return errors;
    }

    private void ValidateTrigger(List<string> errors)
    {
        switch (Trigger)
        {
            case UsageOver u:
                if (!Enum.IsDefined(u.Period) || u.Bytes <= 0) errors.Add("UsageOver requires a valid period and bytes greater than zero.");
                break;
            case HouseholdUsageOver h:
                if (!Enum.IsDefined(h.Period) || h.Bytes <= 0) errors.Add("HouseholdUsageOver requires a valid period and bytes greater than zero.");
                break;
            case TimeWindow w:
                if (!TryTime(w.Start, out var start) || !TryTime(w.End, out var end) || start == end)
                    errors.Add("TimeWindow requires distinct start and end times in HH:mm format.");
                if (w.Days is null || w.Days.Length == 0 || w.Days.Any(d => !Enum.IsDefined(d)))
                    errors.Add("TimeWindow must select at least one valid weekday.");
                break;
            case DeviceIdle i when i.Days <= 0: errors.Add("Idle days must be greater than zero."); break;
            case DeviceIdle or DeviceJoined: break;
            default: errors.Add("Choose a supported trigger."); break;
        }
    }

    internal static bool TryTime(string? value, out TimeOnly time) => TimeOnly.TryParseExact(value, "HH:mm",
        CultureInfo.InvariantCulture, DateTimeStyles.None, out time);

    public static bool IsSafeWireless(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        try { return WirelessNetworkId.Parse(id).Kind is WirelessNetworkKind.Guest or WirelessNetworkKind.Iot; }
        catch (FormatException) { return false; }
    }
}
