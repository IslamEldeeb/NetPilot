namespace NetPilot.Abstractions;

/// <summary>Kbps. Null Download/UploadKbps means unlimited.</summary>
public record SpeedLimit(bool Enabled, int? DownloadKbps, int? UploadKbps)
{
    public static readonly SpeedLimit Unlimited = new(Enabled: false, DownloadKbps: null, UploadKbps: null);
}

public enum ConnectionMedium
{
    Wired,
    Wifi24Ghz,
    Wifi5Ghz,
    Guest,
    Unknown
}

public record ConnectionInfo(ConnectionMedium Medium, bool IsOnline);

/// <summary>Speed limit state as last reported by the router, independent of what NetPilot wants it to be.</summary>
public record SpeedLimitState(bool Enabled, int? DownloadKbps, int? UploadKbps, bool? IsCurrentlyEnforced);

public record RouterInfo(string Model, string FirmwareVersion, string Host);
