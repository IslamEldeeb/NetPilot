using NetPilot.Core.Devices;

namespace NetPilot.Core.Quota;

public enum QuotaScope { Default, Category, Device }
public enum QuotaAction { NotifyOnly, Throttle, Block }

public record QuotaPolicy
{
    public required string Id { get; init; }
    public QuotaScope Scope { get; init; }
    public string? CategoryKey { get; init; }
    public MacAddress? Mac { get; init; }
    public long? DailyBytes { get; init; }
    public long? MonthlyBytes { get; init; }
    public bool Exempt { get; init; }
    public QuotaAction Action { get; init; }
    public int? ThrottleKbps { get; init; }
    public int[] WarnPercents { get; init; } = [80];
    public bool DryRun { get; init; } = true;

    /// <summary>Pure validation; warning thresholds must already be distinct and sorted.</summary>
    public IReadOnlyList<string> Validate()
    {
        List<string> errors = [];
        var expectedId = Scope switch
        {
            QuotaScope.Default when CategoryKey is null && Mac is null => "default",
            QuotaScope.Category when !string.IsNullOrWhiteSpace(CategoryKey) && Mac is null => $"cat:{CategoryKey}",
            QuotaScope.Device when Mac is { } mac && !string.IsNullOrWhiteSpace(mac.ToString()) && CategoryKey is null => $"dev:{mac}",
            _ => null
        };
        if (expectedId is null || Id != expectedId)
            errors.Add("Scope, ID, category and MAC must identify the same quota target.");
        if (DailyBytes is <= 0 || MonthlyBytes is <= 0)
            errors.Add("Caps must be greater than zero when set.");
        if (!Enum.IsDefined(Action))
            errors.Add("Choose a valid action.");
        if (Action == QuotaAction.Throttle && ThrottleKbps is not > 0)
            errors.Add("Throttle requires a positive Kbps limit.");
        if (WarnPercents is null || WarnPercents.Any(p => p is < 1 or > 99)
            || !WarnPercents.SequenceEqual(WarnPercents.Distinct().Order()))
            errors.Add("Warn percents must be distinct, sorted integers from 1 to 99.");
        return errors;
    }
}
