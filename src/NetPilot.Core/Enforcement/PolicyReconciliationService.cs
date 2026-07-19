using NetPilot.Abstractions;
using NetPilot.Core.Devices;
using NetPilot.Core.Policy;

namespace NetPilot.Core.Enforcement;

/// <summary>
/// The brain. Depends only on IRouterProvider (never a concrete SDK) — one read per tick,
/// per-device compare against a cheap fingerprint, write only what's actually wrong.
/// </summary>
public class PolicyReconciliationService(
    IDeviceStore deviceStore,
    IPolicyStore policyStore,
    IActivityLogStore activityLog,
    IDeviceClassifier fallbackClassifier)
{
    public async Task ReconcileAsync(IRouterProvider provider, CancellationToken ct)
    {
        var snapshots = await provider.GetDevicesAsync(ct);
        var seenMacs = new HashSet<string>();

        foreach (var snapshot in snapshots)
        {
            var mac = new MacAddress(snapshot.MacAddress);
            seenMacs.Add(mac);

            var device = await deviceStore.FindByMacAsync(mac, ct);
            var isNewDevice = device is null;

            var categoryKey = ResolveCategoryKey(snapshot, provider.Capabilities, mac);

            device ??= new Device
            {
                Mac = mac,
                FirstSeenAtUtc = DateTimeOffset.UtcNow
            };

            var wasOffline = !device.Connection.IsOnline && !isNewDevice;
            var isNewCategory = await EnsurePolicyExistsAsync(categoryKey, ct);

            device.Hostname = snapshot.Hostname;
            device.IpAddress = snapshot.IpAddress;
            device.CategoryKey = categoryKey;
            device.Connection = snapshot.Connection;
            device.LastSeenAtUtc = DateTimeOffset.UtcNow;

            if (isNewDevice)
            {
                await activityLog.AppendAsync(
                    new ActivityLogEntry(DateTimeOffset.UtcNow, ActivityEventType.DeviceDiscovered, mac,
                        $"New device discovered: {device.Hostname} ({categoryKey})"), ct);
            }
            else if (wasOffline && snapshot.Connection.IsOnline)
            {
                await activityLog.AppendAsync(
                    new ActivityLogEntry(DateTimeOffset.UtcNow, ActivityEventType.DeviceCameBackOnline, mac,
                        $"{device.Hostname} came back online"), ct);
            }

            if (isNewCategory)
            {
                await activityLog.AppendAsync(
                    new ActivityLogEntry(DateTimeOffset.UtcNow, ActivityEventType.NewCategorySeen, mac,
                        $"Never-seen category '{categoryKey}' auto-created with fallback policy"), ct);
            }

            await ApplyPolicyIfNeededAsync(provider, device, snapshot.CurrentLimit, ct);
            await deviceStore.UpsertAsync(device, ct);
        }

        await MarkMissingDevicesOfflineAsync(seenMacs, ct);
    }

    private string ResolveCategoryKey(RouterDeviceSnapshot snapshot, RouterCapabilities capabilities, MacAddress mac)
    {
        if (capabilities.SupportsDeviceCategorization && !string.IsNullOrWhiteSpace(snapshot.RawCategory))
            return DeviceCategory.KeyFrom(snapshot.RawCategory);

        return fallbackClassifier.ClassifyKey(snapshot.Hostname, mac);
    }

    /// <summary>Returns true if this categoryKey had never been seen before (i.e. it's new).</summary>
    private async Task<bool> EnsurePolicyExistsAsync(string categoryKey, CancellationToken ct)
    {
        var existing = await policyStore.FindByCategoryAsync(categoryKey, ct);
        if (existing is not null)
            return false;

        var fallback = new DevicePolicy(categoryKey, SpeedLimit.Unlimited, DefinitionVersion: 1);
        await policyStore.UpsertAsync(fallback, ct);
        return true;
    }

    private async Task ApplyPolicyIfNeededAsync(IRouterProvider provider, Device device, SpeedLimitState routerReportedLimit, CancellationToken ct)
    {
        var policy = await policyStore.FindByCategoryAsync(device.CategoryKey, ct)
            ?? throw new InvalidOperationException($"Policy for category '{device.CategoryKey}' should exist by now.");

        var desiredLimit = device.Override ?? policy.Limit;
        var desiredFingerprint = PolicyFingerprint.Compute(device.CategoryKey, device.Override, policy.DefinitionVersion);

        if (desiredFingerprint == device.LastAppliedFingerprint)
        {
            await activityLog.AppendAsync(
                new ActivityLogEntry(DateTimeOffset.UtcNow, ActivityEventType.PolicySkippedAlreadyCorrect, device.Mac,
                    "Already matches desired policy — no write sent"), ct);
            return;
        }

        // First time we've ever recorded this device (no fingerprint yet): if the router
        // already reports exactly the desired state, adopt that fingerprint without writing
        // — avoids re-sending a limit that's already correct on first discovery.
        if (device.LastAppliedFingerprint is null && MatchesDesiredState(routerReportedLimit, desiredLimit))
        {
            device.LastAppliedFingerprint = desiredFingerprint;
            await activityLog.AppendAsync(
                new ActivityLogEntry(DateTimeOffset.UtcNow, ActivityEventType.PolicySkippedAlreadyCorrect, device.Mac,
                    "Router already reports the desired policy — no write sent"), ct);
            return;
        }

        try
        {
            await provider.SetSpeedLimitAsync(device.Mac, desiredLimit, ct);
            device.LastAppliedFingerprint = desiredFingerprint;
            await activityLog.AppendAsync(
                new ActivityLogEntry(DateTimeOffset.UtcNow, ActivityEventType.PolicyApplied, device.Mac,
                    $"Applied {desiredLimit} for category '{device.CategoryKey}'"), ct);
        }
        catch (Exception ex)
        {
            await activityLog.AppendAsync(
                new ActivityLogEntry(DateTimeOffset.UtcNow, ActivityEventType.WriteFailed, device.Mac,
                    $"SetSpeedLimit failed: {ex.Message}"), ct);
        }
    }

    private static bool MatchesDesiredState(SpeedLimitState routerReported, SpeedLimit desired) =>
        routerReported.Enabled == desired.Enabled
        && routerReported.DownloadKbps == desired.DownloadKbps
        && routerReported.UploadKbps == desired.UploadKbps;

    private async Task MarkMissingDevicesOfflineAsync(HashSet<string> seenMacs, CancellationToken ct)
    {
        var allDevices = await deviceStore.GetAllAsync(ct);
        foreach (var device in allDevices)
        {
            if (seenMacs.Contains(device.Mac) || !device.Connection.IsOnline)
                continue;

            device.Connection = device.Connection with { IsOnline = false };
            await deviceStore.UpsertAsync(device, ct);
            await activityLog.AppendAsync(
                new ActivityLogEntry(DateTimeOffset.UtcNow, ActivityEventType.DeviceWentOffline, device.Mac,
                    $"{device.Hostname} went offline"), ct);
        }
    }
}
