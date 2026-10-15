using NetPilot.Core.Devices;

namespace NetPilot.Core.Enforcement.Claims;

public enum ClaimKind { Throttle, Block }

/// <summary>ThrottleKbps is a symmetric cap on download and upload, in Kbps.</summary>
public record ActionClaim(MacAddress Mac, ClaimKind Kind, string Source, int? ThrottleKbps,
    DateTimeOffset CreatedAtUtc, string? Reason = null)
{
    public string Id => $"{Mac}|{Source}|{Kind}";

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Source))
            throw new ArgumentException("A claim must have an owner.", nameof(Source));
        if (!Enum.IsDefined(Kind) || (Kind == ClaimKind.Throttle ? ThrottleKbps is not > 0 : ThrottleKbps is not null))
            throw new ArgumentException("Throttle claims require a positive Kbps cap; block claims cannot have one.");
    }
}
