namespace NetPilot.Core.Devices;

/// <summary>
/// Data, not a fixed enum: the router's real category vocabulary is larger than any one
/// sample, and a future router brand may report categories this one never does. Any
/// never-seen category is auto-created with a fallback policy rather than rejected.
/// </summary>
public record DeviceCategory(string Key, string DisplayName)
{
    /// <summary>Seeded on first run — the 13 categories confirmed live against the AX53.</summary>
    public static readonly IReadOnlyList<DeviceCategory> SeedCategories =
    [
        new("laptop", "Laptop"),
        new("game_console", "Game Console"),
        new("computer", "Computer"),
        new("ip_camera", "IP Camera"),
        new("desktop", "Desktop"),
        new("mobile", "Mobile"),
        new("smart_device", "Smart Device"),
        new("tablet", "Tablet"),
        new("media_player", "Media Player"),
        new("av_receiver", "AV Receiver"),
        new("smart_meter", "Smart Meter"),
        new("iot_devices", "IoT Devices"),
        new("television", "Television"),
    ];

    /// <summary>Used when a provider reports no RawCategory and the fallback classifier can't guess either.</summary>
    public const string UnknownKey = "unknown";

    /// <summary>Turns a router's free-text category label into a stable dictionary key.</summary>
    public static string KeyFrom(string rawCategory) =>
        rawCategory.Trim().ToLowerInvariant().Replace(' ', '_');
}
