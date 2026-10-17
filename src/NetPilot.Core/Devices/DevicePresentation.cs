using NetPilot.Core.Enforcement.Claims;
using NetPilot.Core.Quota;

namespace NetPilot.Core.Devices;

public enum DeviceSortKey { UsageTodayDesc, Name, LastSeen }

/// <summary>View-model inputs for one device row. Kept free of UI types so the list logic is testable.</summary>
public sealed record DeviceListQuery(
    string? Search,
    string? CategoryKey,
    bool OnlineOnly,
    bool RestrictedOnly,
    DeviceSortKey Sort,
    bool GroupByCategory);

public sealed record DeviceListSnapshot(
    IReadOnlyList<Device> Devices,
    IReadOnlyDictionary<string, long> TodayUsage,
    IReadOnlyDictionary<string, long> MonthUsage,
    IReadOnlyList<QuotaPolicy> Quotas,
    IReadOnlyList<ActionClaim> Claims,
    IReadOnlyCollection<MacAddress> ProtectedMacs,
    DateTimeOffset NowUtc);

public sealed record DeviceListRow(
    Device Device,
    string DisplayName,
    long InternetTodayBytes,
    long InternetMonthBytes,
    bool HasQuota,
    long? QuotaDailyBytes,
    decimal QuotaPercent,
    bool IsBlocked,
    bool IsThrottled,
    bool IsProtected,
    bool IsLanOnly,
    bool IsNew);

/// <summary>
/// Pure device-list projection: friendly-name fallback, today/month Internet usage, the quota
/// meter inputs for the matched policy, and the status flags the UI renders. No I/O and no
/// MudBlazor types, so the same logic can be unit-tested and reused by other shells.
/// </summary>
public static class DeviceList
{
    /// <summary>First seen within this window is surfaced as a "New" device.</summary>
    public static readonly TimeSpan NewDeviceWindow = TimeSpan.FromHours(24);

    public static string DisplayName(Device device) =>
        !string.IsNullOrWhiteSpace(device.FriendlyName) ? device.FriendlyName!
        : !string.IsNullOrWhiteSpace(device.Hostname) ? device.Hostname
        : device.Mac.ToString();

    public static bool MatchesSearch(DeviceListRow row, string? search)
    {
        if (string.IsNullOrWhiteSpace(search)) return true;
        var query = search.Trim();
        return DisplayName(row.Device).Contains(query, StringComparison.OrdinalIgnoreCase)
            || row.Device.Hostname.Contains(query, StringComparison.OrdinalIgnoreCase)
            || row.Device.Mac.ToString().Contains(query, StringComparison.OrdinalIgnoreCase)
            || (MacAddress.TryParse(query, out var mac) && row.Device.Mac == mac)
            || row.Device.IpAddress.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<DeviceListRow> Build(DeviceListSnapshot snapshot) =>
        snapshot.Devices.Select(device => Row(device, snapshot)).ToArray();

    private static DeviceListRow Row(Device device, DeviceListSnapshot snapshot)
    {
        var resolution = QuotaResolver.Resolve(device, snapshot.Quotas);
        var cap = resolution.Kind == QuotaResolutionKind.Policy ? resolution.MatchedPolicy?.DailyBytes : null;
        var used = snapshot.TodayUsage.GetValueOrDefault(device.Mac);
        var deviceClaims = snapshot.Claims.Where(c => c.Mac == device.Mac).ToArray();
        return new(device, DisplayName(device), used, snapshot.MonthUsage.GetValueOrDefault(device.Mac),
            resolution.Kind == QuotaResolutionKind.Policy, cap,
            cap is > 0 ? Math.Clamp((decimal)used * 100m / cap.Value, 0, 100) : 0,
            device.IsBlocked || deviceClaims.Any(c => c.Kind == ClaimKind.Block),
            deviceClaims.Any(c => c.Kind == ClaimKind.Throttle) || device.Override?.Enabled == true || device.QuotaThrottle?.Enabled == true,
            snapshot.ProtectedMacs.Contains(device.Mac), device.UsageLanOnly,
            snapshot.NowUtc >= device.FirstSeenAtUtc && snapshot.NowUtc - device.FirstSeenAtUtc < NewDeviceWindow);
    }

    public static IReadOnlyList<DeviceListRow> Apply(IReadOnlyList<DeviceListRow> rows, DeviceListQuery query)
    {
        List<DeviceListRow> filtered = rows
            .Where(r => string.IsNullOrEmpty(query.CategoryKey) || r.Device.CategoryKey == query.CategoryKey)
            .Where(r => !query.OnlineOnly || r.Device.Connection.IsOnline)
            .Where(r => !query.RestrictedOnly || r.IsBlocked || r.IsThrottled)
            .Where(r => MatchesSearch(r, query.Search))
            .ToList();

        filtered.Sort((a, b) => query.Sort switch
        {
            DeviceSortKey.Name => string.Compare(DisplayName(a.Device), DisplayName(b.Device), StringComparison.OrdinalIgnoreCase),
            DeviceSortKey.LastSeen => b.Device.LastSeenAtUtc.CompareTo(a.Device.LastSeenAtUtc),
            _ => b.InternetTodayBytes.CompareTo(a.InternetTodayBytes)
        });

        return query.GroupByCategory
            ? filtered.OrderBy(row => row.Device.CategoryKey, StringComparer.OrdinalIgnoreCase).ToArray()
            : filtered;
    }

    public static IReadOnlyList<IGrouping<string, DeviceListRow>> Group(IReadOnlyList<DeviceListRow> rows) =>
        rows.GroupBy(r => r.Device.CategoryKey).OrderBy(g => g.Key).ToArray();

    public static (int Online, int Total) Counts(IReadOnlyList<DeviceListRow> rows) =>
        (rows.Count(r => r.Device.Connection.IsOnline), rows.Count);

    public static string DescribeMedium(Abstractions.ConnectionMedium medium) => medium switch
    {
        Abstractions.ConnectionMedium.Wired => "Wired",
        Abstractions.ConnectionMedium.Wifi24Ghz => "2.4G",
        Abstractions.ConnectionMedium.Wifi5Ghz => "5G",
        Abstractions.ConnectionMedium.Guest => "Guest",
        _ => "Unknown"
    };

    public static string DescribeLastSeen(DateTimeOffset lastSeenUtc, DateTimeOffset nowUtc)
    {
        var delta = nowUtc - lastSeenUtc;
        if (delta < TimeSpan.FromMinutes(1)) return "just now";
        if (delta < TimeSpan.FromHours(1)) return $"{(int)delta.TotalMinutes}m ago";
        if (delta < TimeSpan.FromHours(48)) return $"{(int)delta.TotalHours}h ago";
        return $"{(int)delta.TotalDays}d ago";
    }

    public static string DescribeLimit(Abstractions.SpeedLimit limit) =>
        limit.Enabled
            ? $"{limit.DownloadKbps / 1000d:0.#}↓ / {limit.UploadKbps / 1000d:0.#}↑ Mbps"
            : "Unlimited";
}
