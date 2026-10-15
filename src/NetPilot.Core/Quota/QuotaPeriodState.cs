using NetPilot.Abstractions;
using NetPilot.Core.Devices;

namespace NetPilot.Core.Quota;

public enum QuotaPeriodKind { Day, Month }
public enum QuotaActionKind { None, Throttle, Block }

public record QuotaPeriodState
{
    public required MacAddress Mac { get; init; }
    public required QuotaPeriodKind PeriodKind { get; init; }
    public required string PeriodKey { get; init; }
    public string Id => Key(Mac, PeriodKind, PeriodKey);
    public int[] WarnedPercents { get; init; } = [];
    public bool ExceededNotified { get; init; }
    public QuotaActionKind ActionKind { get; init; }
    public DateTimeOffset? ActionAppliedAtUtc { get; init; }
    public long ExtraAllowanceBytes { get; init; }
    public string[] UsedActionTokenHashes { get; init; } = [];
    public bool ReleaseRequested { get; init; }
    public bool ActionSuppressedForPeriod { get; init; }
    public SpeedLimit? PriorLimit { get; init; }
    public string? BlockToken { get; init; }
    public int? AppliedThrottleKbps { get; init; }
    public bool DryRunWouldHaveActed { get; init; }
    public bool ActionSkippedProtected { get; init; }
    public long CapBytes { get; init; }
    public long UsedBytes { get; init; }

    // Decimal arithmetic avoids overflow when combining large caps and allowances.
    public decimal EffectiveCapBytes => (decimal)CapBytes + ExtraAllowanceBytes;
    public decimal Percent => EffectiveCapBytes > 0 ? UsedBytes * 100m / EffectiveCapBytes : 0;

    public static string Key(MacAddress mac, QuotaPeriodKind periodKind, string periodKey) =>
        $"{mac}|{(periodKind == QuotaPeriodKind.Day ? "day" : "month")}|{periodKey}";
}
