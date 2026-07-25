using System.Text.Json.Serialization;

namespace TpLink.Sdk.Models;

/// <summary>
/// Shape of the `new` JSON blob in `admin/access_control?form=black_list` (`operation=insert`),
/// confirmed live from a user-captured curl (see docs/phase4-block-list-live-findings.md).
/// Every field except `conn_type` maps 1:1 onto the matching `TpLinkDeviceRecord` field from the
/// `game_accelerator` read path — `key` in particular is that same record's own `key`, not a
/// freshly generated one; the router UI builds this entry from a device it already knows about.
/// </summary>
public class TpLinkBlockListEntry
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("deviceType")] public string DeviceType { get; set; } = "";
    [JsonPropertyName("mac")] public string Mac { get; set; } = "";
    [JsonPropertyName("ipaddr")] public string IpAddr { get; set; } = "";
    [JsonPropertyName("host")] public string Host { get; set; } = "";
    [JsonPropertyName("conn_type")] public string ConnType { get; set; } = "";
    [JsonPropertyName("key")] public string Key { get; set; } = "";
}
