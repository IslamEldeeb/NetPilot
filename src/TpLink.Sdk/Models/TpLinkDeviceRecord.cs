using System.Text.Json.Serialization;

namespace TpLink.Sdk.Models;

/// <summary>
/// Raw shape of one entry in the `admin/smart_network?form=game_accelerator`
/// (`operation=loadDevice`) response — field names and types match phase1-live-findings.md
/// exactly (camelCase, "on"/"off" strings, Kbps as leniently-typed numbers).
/// </summary>
public class TpLinkDeviceRecord
{
    [JsonPropertyName("mac")] public string Mac { get; set; } = "";
    [JsonPropertyName("ip")] public string Ip { get; set; } = "";
    [JsonPropertyName("host")] public string Host { get; set; } = "";
    [JsonPropertyName("deviceName")] public string? DeviceName { get; set; }
    [JsonPropertyName("deviceType")] public string? DeviceType { get; set; }
    [JsonPropertyName("deviceTag")] public string? DeviceTag { get; set; }
    [JsonPropertyName("isGuest")] public bool IsGuest { get; set; }
    [JsonPropertyName("enableLimit")] public string? EnableLimit { get; set; }

    [JsonPropertyName("downloadLimit")]
    [JsonConverter(typeof(LenientIntConverter))]
    public int? DownloadLimit { get; set; }

    [JsonPropertyName("uploadLimit")]
    [JsonConverter(typeof(LenientIntConverter))]
    public int? UploadLimit { get; set; }

    [JsonPropertyName("enablePriority")] public bool EnablePriority { get; set; }
    [JsonPropertyName("speedLimitOnline")] public bool? SpeedLimitOnline { get; set; }
    [JsonPropertyName("timePeriod")] public int TimePeriod { get; set; }

    public bool IsOnline => !string.Equals(DeviceTag, "offline", StringComparison.OrdinalIgnoreCase);

    public bool IsLimitEnabled => string.Equals(EnableLimit, "on", StringComparison.OrdinalIgnoreCase);
}

public class TpLinkLoadDeviceResponse
{
    [JsonPropertyName("success")] public bool Success { get; set; }
    [JsonPropertyName("data")] public List<TpLinkDeviceRecord> Data { get; set; } = [];
}

public class TpLinkWriteResponse
{
    [JsonPropertyName("success")] public bool Success { get; set; }
}

public class TpLinkMaxValuesData
{
    [JsonPropertyName("max_rules")] public int MaxRules { get; set; }
    [JsonPropertyName("downloadLimitMax")] public int DownloadLimitMax { get; set; }
    [JsonPropertyName("uploadLimitMax")] public int UploadLimitMax { get; set; }
}

public class TpLinkMaxValuesResponse
{
    [JsonPropertyName("success")] public bool Success { get; set; }
    [JsonPropertyName("data")] public TpLinkMaxValuesData? Data { get; set; }
}

public class TpLinkPasswordKeyData
{
    [JsonPropertyName("password")] public List<string> Password { get; set; } = [];
}

public class TpLinkPasswordKeyResponse
{
    [JsonPropertyName("success")] public bool Success { get; set; }
    [JsonPropertyName("data")] public TpLinkPasswordKeyData? Data { get; set; }
}

public class TpLinkLoginData
{
    [JsonPropertyName("stok")] public string Stok { get; set; } = "";
}

public class TpLinkLoginResponse
{
    [JsonPropertyName("success")] public bool Success { get; set; }
    [JsonPropertyName("data")] public TpLinkLoginData? Data { get; set; }
}
