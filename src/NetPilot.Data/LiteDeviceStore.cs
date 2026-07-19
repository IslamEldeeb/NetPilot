using LiteDB;
using NetPilot.Abstractions;
using NetPilot.Core.Devices;
using NetPilot.Data.Documents;

namespace NetPilot.Data;

public class LiteDeviceStore : IDeviceStore
{
    private readonly ILiteCollection<DeviceDocument> _collection;

    public LiteDeviceStore(NetPilotDatabase db)
    {
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
        _collection.Upsert(ToDocument(device));
        return Task.CompletedTask;
    }

    private static Device ToDomain(DeviceDocument doc) => new()
    {
        Mac = new MacAddress(doc.Mac),
        Hostname = doc.Hostname,
        FriendlyName = doc.FriendlyName,
        IpAddress = doc.IpAddress,
        CategoryKey = doc.CategoryKey,
        Connection = new ConnectionInfo(Enum.Parse<ConnectionMedium>(doc.ConnectionMedium), doc.IsOnline),
        Override = doc.OverrideEnabled is null
            ? null
            : new SpeedLimit(doc.OverrideEnabled.Value, doc.OverrideDownloadKbps, doc.OverrideUploadKbps),
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
        ConnectionMedium = device.Connection.Medium.ToString(),
        IsOnline = device.Connection.IsOnline,
        OverrideEnabled = device.Override?.Enabled,
        OverrideDownloadKbps = device.Override?.DownloadKbps,
        OverrideUploadKbps = device.Override?.UploadKbps,
        LastAppliedFingerprint = device.LastAppliedFingerprint,
        FirstSeenAtUtc = device.FirstSeenAtUtc,
        LastSeenAtUtc = device.LastSeenAtUtc
    };
}
