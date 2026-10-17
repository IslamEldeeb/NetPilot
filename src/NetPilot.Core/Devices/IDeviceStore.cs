namespace NetPilot.Core.Devices;

public interface IDeviceStore
{
    Task<Device?> FindByMacAsync(MacAddress mac, CancellationToken ct);
    Task<IReadOnlyList<Device>> GetAllAsync(CancellationToken ct);
    Task UpsertAsync(Device device, CancellationToken ct);
    async Task UpdateIdentityAsync(MacAddress mac, DeviceIdentity identity, CancellationToken ct)
    {
        var device = await FindByMacAsync(mac, ct) ?? throw new InvalidOperationException("Device no longer exists.");
        device.FriendlyName = identity.FriendlyName;
        device.FriendlyNameLocked = identity.FriendlyName is not null;
        device.LocalCategoryKey = identity.CategoryKey;
        device.LocalIconKey = identity.IconKey;
        device.CategoryKey = identity.CategoryKey ?? (string.IsNullOrEmpty(device.RouterCategoryKey) ? device.CategoryKey : device.RouterCategoryKey);
        await UpsertAsync(device, ct);
    }
}

public sealed record DeviceIdentity(string? FriendlyName, string? CategoryKey, string? IconKey);
