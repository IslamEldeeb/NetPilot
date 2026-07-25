using System.Text.Json.Serialization;
using TpLink.Sdk.Models;

namespace TpLink.Sdk.Wireless;

public class TpLinkWirelessResponse<T>
{
    [JsonPropertyName("success")] public bool Success { get; set; }
    [JsonPropertyName("data")] public T? Data { get; set; }
}

/// <summary>
/// Raw shape of `admin/wireless?form=wireless_2g` / `form=wireless_5g` responses — confirmed
/// live per docs/phase3-live-findings.md. Only the fields NetPilot's wireless model actually
/// uses are captured here (the router returns many more legacy 802.11 fields — wep_*, wds_*,
/// wps_state — that nothing in this codebase reads). `channel` is leniently string-typed
/// because it's either a numeric string ("6") or the literal "auto"; `current_channel` is
/// always numeric (the live channel) so it uses the strict lenient-int converter instead.
/// </summary>
public class TpLinkMainBandConfig
{
    [JsonPropertyName("enable")] public string? Enable { get; set; }
    [JsonPropertyName("ssid")] public string? Ssid { get; set; }
    [JsonPropertyName("hidden")] public string? Hidden { get; set; }
    [JsonPropertyName("psk_key")] public string? PskKey { get; set; }
    [JsonPropertyName("encryption")] public string? Encryption { get; set; }

    [JsonPropertyName("channel")]
    [JsonConverter(typeof(LenientStringConverter))]
    public string? Channel { get; set; }

    [JsonPropertyName("current_channel")]
    [JsonConverter(typeof(LenientIntConverter))]
    public int? CurrentChannel { get; set; }

    [JsonPropertyName("txpower")] public string? TxPower { get; set; }
    [JsonPropertyName("htmode")] public string? ChannelWidth { get; set; }
    [JsonPropertyName("macaddr")] public string? MacAddress { get; set; }

    public bool IsEnabled => string.Equals(Enable, "on", StringComparison.OrdinalIgnoreCase);
    public bool IsBroadcasting => !string.Equals(Hidden, "on", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Raw shape of the combined `iot_2g`/`iot_5g`/`iot_5g_2` response — one flat object with
/// prefixed keys, confirmed live. `iot_5g_2` is a currently-empty/disabled third slot
/// (observed with a blank ssid) and is deliberately not modeled — NetPilot has no
/// `WirelessNetworkId` for it and nothing here should crash if it stays blank.
/// </summary>
public class TpLinkIotConfig
{
    [JsonPropertyName("iot_2g_enable")] public string? Iot2gEnable { get; set; }
    [JsonPropertyName("iot_2g_ssid")] public string? Iot2gSsid { get; set; }
    [JsonPropertyName("iot_2g_hidden")] public string? Iot2gHidden { get; set; }
    [JsonPropertyName("iot_2g_psk_key")] public string? Iot2gPskKey { get; set; }
    [JsonPropertyName("iot_2g_encryption")] public string? Iot2gEncryption { get; set; }

    [JsonPropertyName("iot_5g_enable")] public string? Iot5gEnable { get; set; }
    [JsonPropertyName("iot_5g_ssid")] public string? Iot5gSsid { get; set; }
    [JsonPropertyName("iot_5g_hidden")] public string? Iot5gHidden { get; set; }
    [JsonPropertyName("iot_5g_psk_key")] public string? Iot5gPskKey { get; set; }
    [JsonPropertyName("iot_5g_encryption")] public string? Iot5gEncryption { get; set; }

    public bool Is2gEnabled => string.Equals(Iot2gEnable, "on", StringComparison.OrdinalIgnoreCase);
    public bool Is5gEnabled => string.Equals(Iot5gEnable, "on", StringComparison.OrdinalIgnoreCase);
    public bool Is2gBroadcasting => !string.Equals(Iot2gHidden, "on", StringComparison.OrdinalIgnoreCase);
    public bool Is5gBroadcasting => !string.Equals(Iot5gHidden, "on", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Raw shape of the combined `guest_2g`/`guest_5g`/`guest_2g5g` response — same flat,
/// prefixed-key pattern as <see cref="TpLinkIotConfig"/>. The shared `guest_2g5g_*` fields
/// (present when the two guest bands are password-linked) aren't modeled — not needed until
/// wireless writes are implemented.
/// </summary>
public class TpLinkGuestConfig
{
    [JsonPropertyName("guest_2g_enable")] public string? Guest2gEnable { get; set; }
    [JsonPropertyName("guest_2g_ssid")] public string? Guest2gSsid { get; set; }
    [JsonPropertyName("guest_2g_hidden")] public string? Guest2gHidden { get; set; }
    [JsonPropertyName("guest_2g_psk_key")] public string? Guest2gPskKey { get; set; }
    [JsonPropertyName("guest_2g_encryption")] public string? Guest2gEncryption { get; set; }

    [JsonPropertyName("guest_5g_enable")] public string? Guest5gEnable { get; set; }
    [JsonPropertyName("guest_5g_ssid")] public string? Guest5gSsid { get; set; }
    [JsonPropertyName("guest_5g_hidden")] public string? Guest5gHidden { get; set; }
    [JsonPropertyName("guest_5g_psk_key")] public string? Guest5gPskKey { get; set; }
    [JsonPropertyName("guest_5g_encryption")] public string? Guest5gEncryption { get; set; }

    public bool Is2gEnabled => string.Equals(Guest2gEnable, "on", StringComparison.OrdinalIgnoreCase);
    public bool Is5gEnabled => string.Equals(Guest5gEnable, "on", StringComparison.OrdinalIgnoreCase);
    public bool Is2gBroadcasting => !string.Equals(Guest2gHidden, "on", StringComparison.OrdinalIgnoreCase);
    public bool Is5gBroadcasting => !string.Equals(Guest5gHidden, "on", StringComparison.OrdinalIgnoreCase);
}
