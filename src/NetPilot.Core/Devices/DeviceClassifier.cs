namespace NetPilot.Core.Devices;

/// <summary>
/// Fallback classifier for providers that report SupportsDeviceCategorization: false, or
/// a null RawCategory for a specific device. The AX53 doesn't need this (deviceType is
/// reliable) — kept for weaker routers and future brands.
/// </summary>
public interface IDeviceClassifier
{
    string ClassifyKey(string hostname, MacAddress mac);
}

public class HeuristicDeviceClassifier : IDeviceClassifier
{
    private static readonly (string Keyword, string CategoryKey)[] HostnameHints =
    [
        ("iphone", "mobile"), ("android", "mobile"), ("galaxy", "mobile"), ("pixel", "mobile"),
        ("ipad", "tablet"), ("tab-", "tablet"),
        ("appletv", "media_player"), ("chromecast", "media_player"), ("roku", "media_player"), ("firetv", "media_player"),
        ("xbox", "game_console"), ("playstation", "game_console"), ("ps5", "game_console"), ("ps4", "game_console"), ("switch", "game_console"),
        ("cam", "ip_camera"), ("camera", "ip_camera"), ("doorbell", "ip_camera"),
        ("tv", "television"),
        ("laptop", "laptop"), ("macbook", "laptop"),
        ("desktop", "desktop"), ("pc-", "desktop"),
    ];

    public string ClassifyKey(string hostname, MacAddress mac)
    {
        if (string.IsNullOrWhiteSpace(hostname))
            return DeviceCategory.UnknownKey;

        var lower = hostname.ToLowerInvariant();
        foreach (var (keyword, categoryKey) in HostnameHints)
        {
            if (lower.Contains(keyword))
                return categoryKey;
        }

        return DeviceCategory.UnknownKey;
    }
}
