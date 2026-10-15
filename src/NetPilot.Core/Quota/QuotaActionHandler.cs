using System.Security.Cryptography;
using System.Text;
using NetPilot.Core.Devices;
using NetPilot.Core.Enforcement;
using NetPilot.Core.Notifications;
using NetPilot.Core.Usage;

namespace NetPilot.Core.Quota;

public class QuotaActionHandler(IQuotaActionTokenService tokens, IQuotaStateStore states,
    IDeviceStore devices, IActivityLogStore activity, UsagePeriodClock clock)
{
    public async Task<QuotaActionResult> ApplyAsync(string? token, string? action = null, CancellationToken ct = default)
    {
        var validation = tokens.Validate(token);
        if (validation.Status != QuotaActionStatus.Ok || validation.Claim is not { } claim)
            return new(validation.Status, validation.Status == QuotaActionStatus.Expired
                ? "This notification action has expired." : "This notification action is invalid.");
        if (action is not null && action != claim.Action)
            return new(QuotaActionStatus.Invalid, "The action does not match this token.");
        if (claim.Action is not (NotificationActionIds.Extend1Gb or NotificationActionIds.RestoreNow))
            return new(QuotaActionStatus.Invalid, "Unknown notification action.");
        if (await devices.FindByMacAsync(claim.Mac, ct) is null)
            return new(QuotaActionStatus.NotFound, "The device no longer exists.");

        var status = await ApplyToPeriodAsync(token!, claim, ct);
        if (status != QuotaActionStatus.Ok)
            return new(status, status switch
            {
                QuotaActionStatus.AlreadyUsed => "This notification action was already used.",
                QuotaActionStatus.Expired => "This quota period has ended.",
                QuotaActionStatus.Invalid => "The allowance cannot be increased further.",
                _ => "The quota period was not found."
            });

        await LogAcceptedAsync(claim, ct);
        var description = claim.Action == NotificationActionIds.Extend1Gb ? "+1 GB granted for this quota period." : "Restore requested for this quota period.";
        return new(status, description
            + " The Agent applies changes on its next tick (default 180 seconds). Other exceeded quotas may still restrict this device.");
    }

    private async Task<QuotaActionStatus> ApplyToPeriodAsync(string token, QuotaActionClaim claim, CancellationToken ct)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        var status = QuotaActionStatus.NotFound;
        await states.UpdateAsync(QuotaPeriodState.Key(claim.Mac, claim.PeriodKind, claim.PeriodKey), state =>
        {
            if (state is null) return null;
            if (state.UsedActionTokenHashes.Contains(hash)) { status = QuotaActionStatus.AlreadyUsed; return null; }
            // Old notifications cannot change a new quota period.
            var currentKey = claim.PeriodKind == QuotaPeriodKind.Day ? clock.TodayKey() : clock.ThisMonthKey();
            if (claim.PeriodKey != currentKey) { status = QuotaActionStatus.Expired; return null; }
            if (claim.Action == NotificationActionIds.Extend1Gb && state.ExtraAllowanceBytes > long.MaxValue - 1_000_000_000)
            { status = QuotaActionStatus.Invalid; return null; }
            status = QuotaActionStatus.Ok;
            var updated = state with { UsedActionTokenHashes = [.. state.UsedActionTokenHashes, hash] };
            return claim.Action == NotificationActionIds.Extend1Gb
                ? updated with { ExtraAllowanceBytes = state.ExtraAllowanceBytes + 1_000_000_000 }
                : updated with { ReleaseRequested = true };
        }, ct);
        return status;
    }

    private Task LogAcceptedAsync(QuotaActionClaim claim, CancellationToken ct)
    {
        var extend = claim.Action == NotificationActionIds.Extend1Gb;
        return activity.AppendAsync(new(clock.GetUtcNow(), extend ? ActivityEventType.QuotaAllowanceGranted : ActivityEventType.QuotaRestoreRequested,
            claim.Mac, extend ? "Quota +1 GB granted via notification" : "Restore requested via notification"), ct);
    }

}
