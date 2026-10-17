using NetPilot.Core.Devices;
using NetPilot.Core.Enforcement.Claims;
using NetPilot.Core.Quota;
using NetPilot.Core.Usage;

namespace NetPilot.Core.Actions;

public sealed record TemporaryRestrictionRequest(Guid RequestId, MacAddress[] Targets, ClaimKind Kind,
    int DurationMinutes, int? ThrottleKbps = null, bool DryRun = true);
public sealed record TemporaryActionOptions(bool ForceDryRun = true);
public sealed record TemporaryActionResult(int Accepted, int ProtectedSkipped);
public sealed record QuotaBonusRequest(Guid RequestId, MacAddress Mac, QuotaPeriodKind Period,
    string PeriodKey, long Bytes);

public sealed class TemporaryActions(IDeviceStore devices, IProtectedDeviceStore protection,
    IActionClaimStore claims, UsagePeriodClock clock, TemporaryActionOptions? options = null)
{
    public async Task<TemporaryActionResult> RequestAsync(TemporaryRestrictionRequest request, CancellationToken ct)
    {
        if (request.RequestId == Guid.Empty || request.Targets.Length == 0 || request.DurationMinutes is < 1 or > 1440)
            throw new ArgumentException("Choose a device and a duration from 1 to 1440 minutes.");
        var now = clock.GetUtcNow();
        var accepted = 0;
        var skipped = 0;
        var source = $"temporary:{request.RequestId:N}";
        new ActionClaim(request.Targets[0], request.Kind, source, request.ThrottleKbps, now)
        { ExpiresAtUtc = now.AddMinutes(request.DurationMinutes) }.Validate();
        foreach (var mac in request.Targets.Distinct())
        {
            if (await devices.FindByMacAsync(mac, ct) is null) throw new ArgumentException("Device no longer exists.");
            if (await protection.IsProtectedAsync(mac, ct)) { skipped++; continue; }
            var claim = new ActionClaim(mac, request.Kind, source, request.ThrottleKbps, now, "Temporary dashboard action")
            { ExpiresAtUtc = now.AddMinutes(request.DurationMinutes), Pending = true, DryRun = request.DryRun || (options?.ForceDryRun ?? true) };
            claim.Validate();
            // Retrying a saved request must neither extend expiry nor reset an applied claim.
            await claims.TryAddAsync(claim, ct);
            accepted++;
        }
        return new(accepted, skipped);
    }
}

public sealed class QuotaBonusCommands(IDeviceStore devices, IQuotaStore policies,
    IQuotaStateStore states, UsagePeriodClock clock)
{
    public async Task GrantAsync(QuotaBonusRequest request, CancellationToken ct)
    {
        if (request.RequestId == Guid.Empty || request.Bytes <= 0 || !Enum.IsDefined(request.Period))
            throw new ArgumentException("Choose a positive bonus and a Day or Month quota.");
        var currentKey = request.Period == QuotaPeriodKind.Day ? clock.TodayKey() : clock.ThisMonthKey();
        if (request.PeriodKey != currentKey) throw new ArgumentException("This quota period has ended. Refresh and try again.");
        var device = await devices.FindByMacAsync(request.Mac, ct) ?? throw new ArgumentException("Device no longer exists.");
        var resolved = QuotaResolver.Resolve(device, await policies.GetAllAsync(ct));
        var cap = request.Period == QuotaPeriodKind.Day ? resolved.MatchedPolicy?.DailyBytes : resolved.MatchedPolicy?.MonthlyBytes;
        if (resolved.Kind != QuotaResolutionKind.Policy || cap is null) throw new ArgumentException("This device has no cap for the selected quota period.");
        var id = QuotaPeriodState.Key(request.Mac, request.Period, request.PeriodKey);
        var requestKey = $"dashboard:{request.RequestId:N}";
        await states.UpdateAsync(id, current =>
        {
            var keyAtWrite = request.Period == QuotaPeriodKind.Day ? clock.TodayKey() : clock.ThisMonthKey();
            if (request.PeriodKey != keyAtWrite) throw new ArgumentException("This quota period has ended.");
            var state = current ?? new QuotaPeriodState { Mac = request.Mac, PeriodKind = request.Period, PeriodKey = request.PeriodKey, CapBytes = cap.Value };
            if (state.UsedActionTokenHashes.Contains(requestKey)) return state;
            return state with { ExtraAllowanceBytes = checked(state.ExtraAllowanceBytes + request.Bytes),
                UsedActionTokenHashes = [.. state.UsedActionTokenHashes, requestKey] };
        }, ct);
    }
}
