using NetPilot.Core.Devices;

namespace NetPilot.Core.Quota;

public record QuotaActionClaim(MacAddress Mac, QuotaPeriodKind PeriodKind, string PeriodKey, string Action);
public enum QuotaActionStatus { Ok, AlreadyUsed, Invalid, Expired, NotFound }
public record QuotaActionTokenResult(QuotaActionStatus Status, QuotaActionClaim? Claim = null);
public record QuotaActionResult(QuotaActionStatus Status, string Message);

public interface IQuotaActionTokenService
{
    string Create(QuotaActionClaim claim);
    QuotaActionTokenResult Validate(string? token);
}
