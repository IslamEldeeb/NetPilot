using Microsoft.Extensions.DependencyInjection;
using NetPilot.Abstractions;
using NetPilot.Core.Devices;
using NetPilot.Core.Enforcement.Claims;
using NetPilot.Data;

namespace NetPilot.Data.Tests;

public class LiteActionClaimStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"netpilot-claims-{Guid.NewGuid():N}");
    private string DbPath => Path.Combine(_directory, "test.db");
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly MacAddress Mac = new("aa:bb:cc:dd:ee:01");

    public LiteActionClaimStoreTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public async Task ClaimsAndRestorationState_SurviveReopen_AndAreVisibleAcrossSharedHandles()
    {
        var throttle = new ActionClaim(Mac, ClaimKind.Throttle, "quota:day:2026-10-15", 512,
            new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero), "Daily quota exceeded");
        var block = throttle with { Kind = ClaimKind.Block, Source = "rule:test", ThrottleKbps = null, Reason = null };
        var secondDevice = throttle with { Mac = new("aa:bb:cc:dd:ee:02") };
        var reversal = new ClaimDeviceState(Mac, new SpeedLimit(true, 5000, 1000), "opaque-block-token");
        using (var db = new NetPilotDatabase(DbPath))
        {
            var store = new LiteActionClaimStore(db);
            Assert.Empty(await store.GetAllAsync(Ct));
            await store.UpsertAsync(throttle, Ct);
            await store.UpsertAsync(block, Ct);
            await store.UpsertAsync(secondDevice, Ct);
            await store.UpsertDeviceStateAsync(reversal, Ct);
        }
        using var reopened = new NetPilotDatabase(DbPath);
        using var shared = new NetPilotDatabase(DbPath);
        var loaded = new LiteActionClaimStore(reopened);
        var writer = new LiteActionClaimStore(shared);
        Assert.Equal(3, (await loaded.GetAllAsync(Ct)).Count);
        Assert.Equal(2, (await loaded.GetForDeviceAsync(Mac, Ct)).Count);
        Assert.Contains(throttle, await loaded.GetForDeviceAsync(Mac, Ct));
        Assert.Contains(block, await loaded.GetForDeviceAsync(Mac, Ct));
        Assert.Equal(reversal, await loaded.FindDeviceStateAsync(Mac, Ct));
        var edited = throttle with { ThrottleKbps = 128, Reason = "Changed cap" };
        await writer.UpsertAsync(edited, Ct);
        Assert.Equal(2, (await loaded.GetForDeviceAsync(Mac, Ct)).Count);
        Assert.Contains(edited, await loaded.GetForDeviceAsync(Mac, Ct));
        await writer.RemoveAsync(block.Id, Ct);
        Assert.Equal(edited, Assert.Single(await loaded.GetForDeviceAsync(Mac, Ct)));
        await writer.RemoveBySourceAsync(throttle.Source, Ct);
        Assert.Empty(await loaded.GetAllAsync(Ct));
        // Releasing the last claim must not discard the data needed for a failed restore retry.
        Assert.Equal(reversal, await loaded.FindDeviceStateAsync(Mac, Ct));
        var completed = reversal with { PriorLimit = null, BlockToken = null, UnblockProjectionPending = true };
        await writer.UpsertDeviceStateAsync(completed, Ct);
        Assert.Equal(completed, await loaded.FindDeviceStateAsync(Mac, Ct));
    }

    [Fact]
    public async Task DataRegistrationProvidesClaimsStore_AndLegacyDatabaseNeedsNoMigration()
    {
        using var services = new ServiceCollection().AddNetPilotData(DbPath, Path.Combine(_directory, "keys"))
            .BuildServiceProvider();
        var store = services.GetRequiredService<IActionClaimStore>();
        Assert.Empty(await store.GetForDeviceAsync(Mac, Ct));
        Assert.Null(await store.FindDeviceStateAsync(Mac, Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => store.UpsertAsync(
            new(Mac, ClaimKind.Throttle, "rule:test", 0, DateTimeOffset.UtcNow), Ct));
        Assert.Empty(await store.GetAllAsync(Ct));
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
