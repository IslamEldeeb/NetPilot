using NetPilot.Abstractions;
using NetPilot.Core.Devices;
using NetPilot.Core.Enforcement;
using NetPilot.Core.Policy;
using NetPilot.Core.Tests.Fakes;

namespace NetPilot.Core.Tests;

public class PolicyReconciliationServiceTests
{
    private static readonly RouterCapabilities FullCapabilities = new(
        SupportsSpeedLimit: true, SupportsDeviceCategorization: true, SupportsPriorityQos: true, SupportsGuestNetworkInfo: true);

    private static RouterDeviceSnapshot MakeSnapshot(
        string mac = "AA-BB-CC-DD-EE-01", string hostname = "phone-1", string? rawCategory = "Mobile", bool online = true,
        SpeedLimitState? currentLimit = null) =>
        new(mac, "192.168.1.50", hostname, rawCategory,
            new ConnectionInfo(ConnectionMedium.Wifi24Ghz, online),
            // Mismatches the default Unlimited fallback policy on purpose, so tests using the
            // default exercise the write path rather than silently hitting the new
            // already-correct-on-first-discovery skip.
            currentLimit ?? new SpeedLimitState(true, 9999, 9999, null));

    private static (PolicyReconciliationService Service, FakeRouterProvider Provider, InMemoryDeviceStore Devices,
        InMemoryPolicyStore Policies, InMemoryActivityLogStore Log) Build(IDeviceClassifier? classifier = null)
    {
        var provider = new FakeRouterProvider(FullCapabilities);
        var devices = new InMemoryDeviceStore();
        var policies = new InMemoryPolicyStore();
        var log = new InMemoryActivityLogStore();
        var service = new PolicyReconciliationService(devices, policies, log, classifier ?? new HeuristicDeviceClassifier());
        return (service, provider, devices, policies, log);
    }

    [Fact]
    public async Task NewDevice_IsDiscovered_AndCategoryAutoCreated_AndPolicyApplied()
    {
        var (service, provider, devices, _, log) = Build();
        provider.Devices.Add(MakeSnapshot());

        await service.ReconcileAsync(provider, CancellationToken.None);

        var device = await devices.FindByMacAsync(new MacAddress("AA-BB-CC-DD-EE-01"), CancellationToken.None);
        Assert.NotNull(device);
        Assert.Equal("mobile", device!.CategoryKey);

        Assert.Contains(log.Entries, e => e.Type == ActivityEventType.DeviceDiscovered);
        Assert.Contains(log.Entries, e => e.Type == ActivityEventType.NewCategorySeen);
        Assert.Single(provider.AppliedLimits);
    }

    [Fact]
    public async Task SecondReconcile_SameState_SkipsWrite_ViaFingerprint()
    {
        var (service, provider, _, _, log) = Build();
        provider.Devices.Add(MakeSnapshot());

        await service.ReconcileAsync(provider, CancellationToken.None);
        Assert.Single(provider.AppliedLimits);

        await service.ReconcileAsync(provider, CancellationToken.None);

        Assert.Single(provider.AppliedLimits); // no second write
        Assert.Contains(log.Entries, e => e.Type == ActivityEventType.PolicySkippedAlreadyCorrect);
    }

    [Fact]
    public async Task EditingPolicy_BumpsVersion_ForcesReapplyOnNextTick()
    {
        var (service, provider, _, policies, _) = Build();
        provider.Devices.Add(MakeSnapshot());

        await service.ReconcileAsync(provider, CancellationToken.None);
        Assert.Single(provider.AppliedLimits);

        var policy = await policies.FindByCategoryAsync("mobile", CancellationToken.None);
        var edited = policy!.WithLimit(new SpeedLimit(true, 5000, 1000));
        await policies.UpsertAsync(edited, CancellationToken.None);

        await service.ReconcileAsync(provider, CancellationToken.None);

        Assert.Equal(2, provider.AppliedLimits.Count);
        Assert.Equal(5000, provider.AppliedLimits[1].Limit.DownloadKbps);
    }

    [Fact]
    public async Task DeviceOverride_TakesPrecedenceOverCategoryPolicy()
    {
        var (service, provider, devices, policies, _) = Build();
        provider.Devices.Add(MakeSnapshot());

        await service.ReconcileAsync(provider, CancellationToken.None);

        var device = (await devices.FindByMacAsync(new MacAddress("AA-BB-CC-DD-EE-01"), CancellationToken.None))!;
        device.Override = new SpeedLimit(true, 999, 111);
        await devices.UpsertAsync(device, CancellationToken.None);

        await service.ReconcileAsync(provider, CancellationToken.None);

        var lastApplied = provider.AppliedLimits.Last();
        Assert.Equal(999, lastApplied.Limit.DownloadKbps);
        Assert.Equal(111, lastApplied.Limit.UploadKbps);
    }

    [Fact]
    public async Task DeviceMissingFromNextPoll_IsMarkedOffline_OnlyOnce()
    {
        var (service, provider, devices, _, log) = Build();
        provider.Devices.Add(MakeSnapshot());
        await service.ReconcileAsync(provider, CancellationToken.None);

        provider.Devices.Clear();
        await service.ReconcileAsync(provider, CancellationToken.None);

        var device = await devices.FindByMacAsync(new MacAddress("AA-BB-CC-DD-EE-01"), CancellationToken.None);
        Assert.False(device!.Connection.IsOnline);
        Assert.Single(log.Entries, e => e.Type == ActivityEventType.DeviceWentOffline);

        // Still missing next tick — must not log DeviceWentOffline again.
        await service.ReconcileAsync(provider, CancellationToken.None);
        Assert.Single(log.Entries, e => e.Type == ActivityEventType.DeviceWentOffline);
    }

    [Fact]
    public async Task FallbackClassifier_UsedWhenProviderDoesNotSupportCategorization()
    {
        var noCategoryCapabilities = FullCapabilities with { SupportsDeviceCategorization = false };
        var provider = new FakeRouterProvider(noCategoryCapabilities);
        provider.Devices.Add(MakeSnapshot(rawCategory: null));

        var devices = new InMemoryDeviceStore();
        var policies = new InMemoryPolicyStore();
        var log = new InMemoryActivityLogStore();
        var service = new PolicyReconciliationService(devices, policies, log, new FixedCategoryClassifier("television"));

        await service.ReconcileAsync(provider, CancellationToken.None);

        var device = await devices.FindByMacAsync(new MacAddress("AA-BB-CC-DD-EE-01"), CancellationToken.None);
        Assert.Equal("television", device!.CategoryKey);
    }

    [Fact]
    public async Task NewDevice_AlreadyMatchingRouterState_SkipsInitialWrite()
    {
        var (service, provider, devices, _, log) = Build();
        // Router already reports exactly the default Unlimited fallback policy.
        provider.Devices.Add(MakeSnapshot(currentLimit: new SpeedLimitState(false, null, null, null)));

        await service.ReconcileAsync(provider, CancellationToken.None);

        Assert.Empty(provider.AppliedLimits);
        Assert.Contains(log.Entries, e => e.Type == ActivityEventType.PolicySkippedAlreadyCorrect);

        var device = await devices.FindByMacAsync(new MacAddress("AA-BB-CC-DD-EE-01"), CancellationToken.None);
        Assert.NotNull(device!.LastAppliedFingerprint); // fingerprint adopted so later ticks can still detect real drift
    }

    [Fact]
    public async Task WriteFailure_IsLogged_AndFingerprintNotAdvanced_SoItRetriesNextTick()
    {
        var (service, provider, devices, _, log) = Build();
        provider.Devices.Add(MakeSnapshot());
        provider.ThrowOnNextWrite = true;

        await service.ReconcileAsync(provider, CancellationToken.None);

        Assert.Empty(provider.AppliedLimits);
        Assert.Contains(log.Entries, e => e.Type == ActivityEventType.WriteFailed);

        var device = await devices.FindByMacAsync(new MacAddress("AA-BB-CC-DD-EE-01"), CancellationToken.None);
        Assert.Null(device!.LastAppliedFingerprint);

        await service.ReconcileAsync(provider, CancellationToken.None);
        Assert.Single(provider.AppliedLimits); // retried and succeeded this time
    }
}
