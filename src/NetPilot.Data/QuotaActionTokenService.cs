using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using NetPilot.Core.Devices;
using NetPilot.Core.Notifications;
using NetPilot.Core.Quota;
using NetPilot.Core.Usage;

namespace NetPilot.Data;

public sealed class QuotaActionTokenService : IQuotaActionTokenService
{
    private readonly IDataProtector _envelope;
    private readonly ITimeLimitedDataProtector _timed;
    private readonly UsagePeriodClock _clock;
    private readonly TimeSpan _lifetime;

    public QuotaActionTokenService(IDataProtectionProvider provider, NotificationOptions options, UsagePeriodClock clock)
    {
        options.Validate();
        _envelope = provider.CreateProtector("NetPilot.QuotaActions", "envelope-v1");
        _timed = provider.CreateProtector("NetPilot.QuotaActions", "claim-v1").ToTimeLimitedDataProtector();
        _clock = clock;
        _lifetime = TimeSpan.FromHours(options.ActionTokenHours);
    }

    public string Create(QuotaActionClaim claim)
    {
        var expires = _clock.GetUtcNow().Add(_lifetime);
        var payload = JsonSerializer.Serialize(new TokenPayload(claim.Mac, claim.PeriodKind, claim.PeriodKey, claim.Action));
        // An authenticated expiry envelope lets us distinguish expiry from tampering without parsing DP internals.
        return _envelope.Protect(expires.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture) + "|" + _timed.Protect(payload, expires));
    }

    public QuotaActionTokenResult Validate(string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 4096) return new(QuotaActionStatus.Invalid);
        try
        {
            // Reject alternate base64 encodings so the token hash remains a unique replay key.
            if (WebEncoders.Base64UrlEncode(WebEncoders.Base64UrlDecode(token)) != token)
                return new(QuotaActionStatus.Invalid);
            var envelope = _envelope.Unprotect(token);
            var separator = envelope.IndexOf('|');
            if (separator < 0 || !long.TryParse(envelope.AsSpan(0, separator), CultureInfo.InvariantCulture, out var expires))
                return new(QuotaActionStatus.Invalid);
            if (_clock.GetUtcNow().ToUnixTimeMilliseconds() >= expires) return new(QuotaActionStatus.Expired);
            var payload = JsonSerializer.Deserialize<TokenPayload>(_timed.Unprotect(envelope[(separator + 1)..]));
            if (payload is null || !Enum.IsDefined(payload.PeriodKind) ||
                payload.Action is not (NotificationActionIds.Extend1Gb or NotificationActionIds.RestoreNow) ||
                !DateTime.TryParseExact(payload.PeriodKey, payload.PeriodKind == QuotaPeriodKind.Day ? "yyyy-MM-dd" : "yyyy-MM",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) return new(QuotaActionStatus.Invalid);
            return new(QuotaActionStatus.Ok, new(new MacAddress(payload.Mac), payload.PeriodKind, payload.PeriodKey, payload.Action));
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or ArgumentException or FormatException)
        {
            return new(QuotaActionStatus.Invalid);
        }
    }

    private sealed record TokenPayload(string Mac, QuotaPeriodKind PeriodKind, string PeriodKey, string Action);
}
