using LiteDB;
using NetPilot.Abstractions;
using NetPilot.Core.Devices;
using NetPilot.Data.Documents;

namespace NetPilot.Data;

public class LiteDeviceStore : IDeviceStore
{
    private readonly ILiteCollection<DeviceDocument> _collection;
    private readonly NetPilotDatabase _db;

    public LiteDeviceStore(NetPilotDatabase db)
    {
        _db = db;
        _collection = db.GetCollection<DeviceDocument>("devices");
    }

    public Task<Device?> FindByMacAsync(MacAddress mac, CancellationToken ct)
    {
        var doc = _collection.FindById((string)mac);
        return Task.FromResult(doc is null ? null : ToDomain(doc));
    }

    public Task<IReadOnlyList<Device>> GetAllAsync(CancellationToken ct)
    {
        IReadOnlyList<Device> devices = _collection.FindAll().Select(ToDomain).ToList();
        return Task.FromResult(devices);
    }

    public Task UpsertAsync(Device device, CancellationToken ct)
    {
        _db.InTransaction(() =>
        {
            var incoming = ToDocument(device);
            var saved = _collection.FindById((string)device.Mac);
            if (saved is not null)
            {
                // A snapshot may predate a Web edit; only UpdateIdentityAsync owns these fields.
                incoming.FriendlyName = saved.FriendlyName;
                incoming.FriendlyNameLocked = saved.FriendlyNameLocked;
                incoming.LocalCategoryKey = saved.LocalCategoryKey;
                incoming.LocalIconKey = saved.LocalIconKey;
                incoming.CategoryKey = saved.LocalCategoryKey
                    ?? (string.IsNullOrEmpty(incoming.RouterCategoryKey) ? incoming.CategoryKey : incoming.RouterCategoryKey);
            }
            _collection.Upsert(incoming);
        });
        return Task.CompletedTask;
    }

    public Task UpdateIdentityAsync(MacAddress mac, DeviceIdentity identity, CancellationToken ct)
    {
        _db.InTransaction(() =>
        {
            var saved = _collection.FindById((string)mac) ?? throw new InvalidOperationException("Device no longer exists.");
            saved.FriendlyName = identity.FriendlyName;
            saved.FriendlyNameLocked = identity.FriendlyName is not null;
            saved.LocalCategoryKey = identity.CategoryKey;
            saved.LocalIconKey = identity.IconKey;
            saved.CategoryKey = identity.CategoryKey ?? (string.IsNullOrEmpty(saved.RouterCategoryKey) ? saved.CategoryKey : saved.RouterCategoryKey);
            _collection.Update(saved);
        });
        return Task.CompletedTask;
    }

    private static Device ToDomain(DeviceDocument doc) => new()
    {
        Mac = new MacAddress(doc.Mac),
        Hostname = doc.Hostname,
        FriendlyName = doc.FriendlyName,
        IpAddress = doc.IpAddress,
        CategoryKey = doc.CategoryKey,
        RouterCategoryKey = doc.RouterCategoryKey ?? "",
        LocalCategoryKey = doc.LocalCategoryKey,
        FriendlyNameLocked = doc.FriendlyNameLocked,
        LocalIconKey = doc.LocalIconKey,
        Connection = new ConnectionInfo(Enum.Parse<ConnectionMedium>(doc.ConnectionMedium), doc.IsOnline),
        Override = doc.OverrideEnabled is null
            ? null
            : new SpeedLimit(doc.OverrideEnabled.Value, doc.OverrideDownloadKbps, doc.OverrideUploadKbps),
        QuotaThrottle = doc.QuotaThrottleEnabled is null
            ? null
            : new SpeedLimit(doc.QuotaThrottleEnabled.Value, doc.QuotaThrottleDownloadKbps, doc.QuotaThrottleUploadKbps),
        RouterReportedLimit = doc.RouterLimitEnabled is null
            ? null
            : new SpeedLimitState(doc.RouterLimitEnabled.Value, doc.RouterDownloadKbps, doc.RouterUploadKbps, doc.RouterLimitCurrentlyEnforced),
        UsageLanOnly = doc.UsageLanOnly,
        IsBlocked = doc.IsBlocked,
        BlockListToken = doc.BlockListToken,
        LastAppliedFingerprint = doc.LastAppliedFingerprint,
        FirstSeenAtUtc = doc.FirstSeenAtUtc,
        LastSeenAtUtc = doc.LastSeenAtUtc
    };

    private static DeviceDocument ToDocument(Device device) => new()
    {
        Mac = device.Mac,
        Hostname = device.Hostname,
        FriendlyName = device.FriendlyName,
        IpAddress = device.IpAddress,
        CategoryKey = device.CategoryKey,
        RouterCategoryKey = device.RouterCategoryKey,
        LocalCategoryKey = device.LocalCategoryKey,
        FriendlyNameLocked = device.FriendlyNameLocked,
        LocalIconKey = device.LocalIconKey,
        ConnectionMedium = device.Connection.Medium.ToString(),
        IsOnline = device.Connection.IsOnline,
        OverrideEnabled = device.Override?.Enabled,
        OverrideDownloadKbps = device.Override?.DownloadKbps,
        OverrideUploadKbps = device.Override?.UploadKbps,
        QuotaThrottleEnabled = device.QuotaThrottle?.Enabled,
        QuotaThrottleDownloadKbps = device.QuotaThrottle?.DownloadKbps,
        QuotaThrottleUploadKbps = device.QuotaThrottle?.UploadKbps,
        RouterLimitEnabled = device.RouterReportedLimit?.Enabled,
        RouterDownloadKbps = device.RouterReportedLimit?.DownloadKbps,
        RouterUploadKbps = device.RouterReportedLimit?.UploadKbps,
        RouterLimitCurrentlyEnforced = device.RouterReportedLimit?.IsCurrentlyEnforced,
        UsageLanOnly = device.UsageLanOnly,
        IsBlocked = device.IsBlocked,
        BlockListToken = device.BlockListToken,
        LastAppliedFingerprint = device.LastAppliedFingerprint,
        FirstSeenAtUtc = device.FirstSeenAtUtc,
        LastSeenAtUtc = device.LastSeenAtUtc
    };
}
