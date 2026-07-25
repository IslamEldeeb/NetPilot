using System.Text.Json.Serialization;

namespace TpLink.Sdk.Models;

/// <summary>Confirmed live per docs/phase3-live-findings.md — plain JSON, no envelope.</summary>
public class TpLinkFirmwareInfo
{
    [JsonPropertyName("model")] public string? Model { get; set; }
    [JsonPropertyName("firmware_version")] public string? FirmwareVersion { get; set; }
    [JsonPropertyName("hardware_version")] public string? HardwareVersion { get; set; }
}

public class TpLinkFirmwareInfoResponse
{
    [JsonPropertyName("success")] public bool Success { get; set; }
    [JsonPropertyName("data")] public TpLinkFirmwareInfo? Data { get; set; }
}
