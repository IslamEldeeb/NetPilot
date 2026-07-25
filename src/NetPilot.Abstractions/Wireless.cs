namespace NetPilot.Abstractions;

public enum WirelessBand { Ghz24, Ghz5, Ghz6 }

public enum WirelessNetworkKind { Main, Guest, Iot }

/// <summary>
/// Stable, provider-independent identifier for one wireless network. Used as the LiteDB key,
/// the REST path segment, and the Home Assistant unique_id — so it must never change shape
/// once shipped. Format: "{kind}-{band}", lowercase: main-2.4g, main-5g, iot-2.4g, guest-5g.
/// </summary>
public readonly record struct WirelessNetworkId(WirelessNetworkKind Kind, WirelessBand Band)
{
    public override string ToString() => $"{Kind.ToString().ToLowerInvariant()}-{BandToken(Band)}";

    public static string BandToken(WirelessBand b) => b switch
    {
        WirelessBand.Ghz24 => "2.4g",
        WirelessBand.Ghz5 => "5g",
        WirelessBand.Ghz6 => "6g",
        _ => "unknown"
    };

    public static WirelessNetworkId Parse(string id)
    {
        var parts = id.Split('-', 2);
        if (parts.Length != 2)
            throw new FormatException($"'{id}' is not a valid WirelessNetworkId (expected '{{kind}}-{{band}}').");

        var kind = parts[0] switch
        {
            "main" => WirelessNetworkKind.Main,
            "guest" => WirelessNetworkKind.Guest,
            "iot" => WirelessNetworkKind.Iot,
            _ => throw new FormatException($"'{id}' has an unrecognized wireless network kind '{parts[0]}'.")
        };

        var band = parts[1] switch
        {
            "2.4g" => WirelessBand.Ghz24,
            "5g" => WirelessBand.Ghz5,
            "6g" => WirelessBand.Ghz6,
            _ => throw new FormatException($"'{id}' has an unrecognized wireless band '{parts[1]}'.")
        };

        return new WirelessNetworkId(kind, band);
    }
}

/// <summary>What the router currently reports. Read-only observation, never intent.</summary>
public record WirelessNetworkState(
    WirelessNetworkId Id,
    string DisplayName,
    bool Enabled,
    string? Ssid,
    bool? SsidBroadcast,
    string? SecurityMode,
    int? Channel,
    string? ChannelWidth,
    string? TxPower,
    WirelessNetworkFeatures Features,
    IReadOnlyList<WirelessNetworkId> CoupledWith);

/// <summary>
/// Per-network capability flags — not per-router. The AX53's guest network may allow an
/// SSID change but not a channel change; HA must only create entities for what's writable.
/// CoupledWith on the state above carries the W4 finding: toggling this network also
/// affects those. Surfaced to the UI and API so "turn off 2.4 GHz" can warn that IoT
/// goes with it, instead of the user discovering that when their sensors drop off.
/// </summary>
public record WirelessNetworkFeatures(
    bool CanToggle,
    bool CanEditSsid,
    bool CanEditPassword,
    bool CanEditChannel,
    bool CanEditChannelWidth,
    bool CanEditTxPower,
    bool CanEditBroadcast);

/// <summary>
/// A sparse delta. Null means "leave alone" — never "clear". Providers must read-modify-write
/// so a null here can never blank a field on a whole-object firmware write.
/// </summary>
public record WirelessNetworkUpdate
{
    public bool? Enabled { get; init; }
    public string? Ssid { get; init; }
    public string? Password { get; init; }
    public bool? SsidBroadcast { get; init; }
    public int? Channel { get; init; }
    public string? ChannelWidth { get; init; }
    public string? TxPower { get; init; }
}
