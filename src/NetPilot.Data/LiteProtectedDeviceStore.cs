using LiteDB;
using NetPilot.Core.Devices;
using NetPilot.Core.Quota;
using NetPilot.Data.Documents;

namespace NetPilot.Data;

public class LiteProtectedDeviceStore(NetPilotDatabase db) : IProtectedDeviceStore
{
    private readonly ILiteCollection<ProtectedDeviceDocument> _collection = db.GetCollection<ProtectedDeviceDocument>("protected_devices");

    public Task<IReadOnlyList<MacAddress>> GetAllAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<MacAddress>>(_collection.FindAll().Select(d => new MacAddress(d.Mac)).ToList());

    public Task AddAsync(MacAddress mac, CancellationToken ct)
    {
        _collection.Upsert(new ProtectedDeviceDocument { Mac = mac });
        return Task.CompletedTask;
    }

    public Task RemoveAsync(MacAddress mac, CancellationToken ct)
    {
        _collection.Delete((string)mac);
        return Task.CompletedTask;
    }

    public Task<bool> IsProtectedAsync(MacAddress mac, CancellationToken ct) =>
        Task.FromResult(_collection.FindById((string)mac) is not null);
}
