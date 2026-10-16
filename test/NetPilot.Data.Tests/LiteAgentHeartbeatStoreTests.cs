using Microsoft.Extensions.DependencyInjection;
using NetPilot.Core.Health;

namespace NetPilot.Data.Tests;

public class LiteAgentHeartbeatStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"netpilot-heartbeat-{Guid.NewGuid():N}");
    private string DbPath => Path.Combine(_directory, "test.db");
    private static readonly CancellationToken Ct = CancellationToken.None;
    public LiteAgentHeartbeatStoreTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public async Task SingleHeartbeatIsVisibleAcrossHandlesAndSurvivesReopen()
    {
        var started = new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);
        var heartbeat = new AgentHeartbeat { LastTickStartedUtc = started, TickIntervalSeconds = 180,
            EffectiveModes = new(true, true), AppVersion = "1.2.3" };
        using (var services = new ServiceCollection().AddNetPilotData(DbPath, Path.Combine(_directory, "keys")).BuildServiceProvider())
        using (var reader = new NetPilotDatabase(DbPath))
        {
            var writer = services.GetRequiredService<IAgentHeartbeatStore>();
            var observer = new LiteAgentHeartbeatStore(reader);
            Assert.Null(await observer.GetAsync(Ct));
            await writer.SaveAsync(heartbeat, Ct);
            Assert.Equal(heartbeat, await observer.GetAsync(Ct));
            heartbeat = heartbeat with { LastTickCompletedUtc = started.AddSeconds(2), LastTickDurationMs = 2000.5,
                LastSuccessfulRouterReadUtc = started.AddSeconds(1), ConsecutiveFailedTicks = 2, LastErrorSummary = "Router operation failed" };
            await writer.SaveAsync(heartbeat, Ct);
            await writer.SaveAsync(heartbeat, Ct);
            Assert.Equal(heartbeat, await observer.GetAsync(Ct));
            Assert.Equal(1, reader.GetCollection<LiteAgentHeartbeatStore.HeartbeatDocument>("agent_heartbeat").Count());
        }
        using var reopened = new NetPilotDatabase(DbPath);
        Assert.Equal(heartbeat, await new LiteAgentHeartbeatStore(reopened).GetAsync(Ct));
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
