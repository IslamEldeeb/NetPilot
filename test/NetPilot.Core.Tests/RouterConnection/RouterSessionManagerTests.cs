using Microsoft.Extensions.Logging.Abstractions;
using NetPilot.Abstractions;
using NetPilot.Core.RouterConnection;
using NetPilot.Core.Tests.Fakes;

namespace NetPilot.Core.Tests.RouterConnection;

public class RouterSessionManagerTests
{
    private static readonly RouterCapabilities FullCapabilities = new()
    {
        SupportsSpeedLimit = true,
        SupportsDeviceCategorization = true
    };

    private static (RouterSessionManager Manager, FakeRouterProvider Provider, InMemoryRouterConnectionStore Store) Build()
    {
        var provider = new FakeRouterProvider(FullCapabilities);
        var store = new InMemoryRouterConnectionStore();
        var cipher = new FakePasswordCipher();
        var manager = new RouterSessionManager(provider, store, cipher, NullLogger<RouterSessionManager>.Instance);
        return (manager, provider, store);
    }

    [Fact]
    public async Task ExecuteAsync_NoConnectionSaved_ThrowsRouterNotConfigured()
    {
        var (manager, _, _) = Build();

        await Assert.ThrowsAsync<RouterNotConfiguredException>(
            () => manager.ExecuteAsync(p => p.GetRouterInfoAsync(CancellationToken.None), CancellationToken.None));
    }

    [Fact]
    public async Task ExecuteAsync_ConnectsOnce_ThenReusesSessionAcrossCalls()
    {
        var (manager, provider, store) = Build();
        await store.SaveAsync(new NetPilot.Core.RouterConnection.RouterConnection("fake", "192.168.1.1", true, "admin", "secret"), CancellationToken.None);

        await manager.ExecuteAsync(p => p.GetRouterInfoAsync(CancellationToken.None), CancellationToken.None);
        await manager.ExecuteAsync(p => p.GetRouterInfoAsync(CancellationToken.None), CancellationToken.None);
        await manager.ExecuteAsync(p => p.GetRouterInfoAsync(CancellationToken.None), CancellationToken.None);

        Assert.Equal(1, provider.ConnectCallCount);
    }

    [Fact]
    public async Task ExecuteAsync_ActionThrows_InvalidatesSession_NextCallReconnects()
    {
        var (manager, provider, store) = Build();
        await store.SaveAsync(new NetPilot.Core.RouterConnection.RouterConnection("fake", "192.168.1.1", true, "admin", "secret"), CancellationToken.None);

        await manager.ExecuteAsync(p => p.GetRouterInfoAsync(CancellationToken.None), CancellationToken.None);
        Assert.Equal(1, provider.ConnectCallCount);

        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.ExecuteAsync<object>(_ =>
            throw new InvalidOperationException("simulated failure mid-call"), CancellationToken.None));

        // The failed call invalidated the session, so the next call must reconnect.
        await manager.ExecuteAsync(p => p.GetRouterInfoAsync(CancellationToken.None), CancellationToken.None);
        Assert.Equal(2, provider.ConnectCallCount);
    }

    [Fact]
    public async Task TestConnectionAsync_UsesSuppliedSettings_NotStoredConnection()
    {
        var (manager, provider, store) = Build();
        await store.SaveAsync(new NetPilot.Core.RouterConnection.RouterConnection("fake", "stored-host", true, "admin", "stored-secret"), CancellationToken.None);

        var testSettings = new RouterConnectionSettings("unsaved-host", true, "admin", "unsaved-password");
        await manager.TestConnectionAsync(testSettings, p => p.GetRouterInfoAsync(CancellationToken.None), CancellationToken.None);

        Assert.Single(provider.ConnectCalls, s => s.Host == "unsaved-host");
    }

    [Fact]
    public async Task TestConnectionAsync_AlwaysInvalidatesSession_NextExecuteReconnects()
    {
        var (manager, provider, store) = Build();
        await store.SaveAsync(new NetPilot.Core.RouterConnection.RouterConnection("fake", "stored-host", true, "admin", "stored-secret"), CancellationToken.None);

        var testSettings = new RouterConnectionSettings("unsaved-host", true, "admin", "unsaved-password");
        await manager.TestConnectionAsync(testSettings, p => p.GetRouterInfoAsync(CancellationToken.None), CancellationToken.None);
        Assert.Equal(1, provider.ConnectCallCount);

        // Even though TestConnectionAsync just connected, the next ExecuteAsync must
        // re-authenticate with the real stored connection rather than trust the test session.
        await manager.ExecuteAsync(p => p.GetRouterInfoAsync(CancellationToken.None), CancellationToken.None);
        Assert.Equal(2, provider.ConnectCallCount);
        Assert.Equal("stored-host", provider.ConnectCalls[1].Host);
    }

    [Fact]
    public async Task ConcurrentExecuteAsync_CallsAreSerialized()
    {
        var (manager, provider, store) = Build();
        await store.SaveAsync(new NetPilot.Core.RouterConnection.RouterConnection("fake", "192.168.1.1", true, "admin", "secret"), CancellationToken.None);

        var concurrentCount = 0;
        var maxObservedConcurrency = 0;
        var gate = new object();

        async Task<bool> TrackedAction(IRouterProvider p)
        {
            lock (gate)
            {
                concurrentCount++;
                maxObservedConcurrency = Math.Max(maxObservedConcurrency, concurrentCount);
            }
            await Task.Delay(20);
            lock (gate)
            {
                concurrentCount--;
            }
            return true;
        }

        var tasks = Enumerable.Range(0, 5)
            .Select(_ => manager.ExecuteAsync(TrackedAction, CancellationToken.None))
            .ToArray();
        await Task.WhenAll(tasks);

        Assert.Equal(1, maxObservedConcurrency);
    }
}
