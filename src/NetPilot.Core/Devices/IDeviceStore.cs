namespace NetPilot.Core.Devices;

public interface IDeviceStore
{
    Task<Device?> FindByMacAsync(MacAddress mac, CancellationToken ct);
    Task<IReadOnlyList<Device>> GetAllAsync(CancellationToken ct);
    Task UpsertAsync(Device device, CancellationToken ct);
}
