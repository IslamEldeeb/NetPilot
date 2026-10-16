using NetPilot.Core.Health;

namespace NetPilot.Core.Tests;

public class AgentHealthTests
{
    private static readonly DateTimeOffset Tick = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);
    private static AgentHeartbeat Heartbeat => new()
    {
        LastTickStartedUtc = Tick.AddSeconds(-1), LastTickCompletedUtc = Tick, TickIntervalSeconds = 30
    };

    [Theory]
    [InlineData(0, AgentHealthStatus.Healthy)]
    [InlineData(120, AgentHealthStatus.Healthy)]
    [InlineData(120.001, AgentHealthStatus.Stale)]
    [InlineData(210, AgentHealthStatus.Stale)]
    [InlineData(210.001, AgentHealthStatus.Down)]
    public void HealthUsesStrictTwoAndFiveIntervalThresholds(double age, AgentHealthStatus expected)
    {
        var clock = new FakeClock(Tick.AddSeconds(age));
        Assert.Equal(expected, AgentHealth.Evaluate(Heartbeat, clock.GetUtcNow()));
    }

    [Fact]
    public void MissingInvalidAndFutureHeartbeatsCannotProveHealth()
    {
        var now = new FakeClock(Tick).GetUtcNow();
        Assert.Equal(AgentHealthStatus.Unknown, AgentHealth.Evaluate(null, now));
        Assert.Equal(AgentHealthStatus.Unknown, AgentHealth.Evaluate(Heartbeat with { TickIntervalSeconds = 0 }, now));
        Assert.Equal(AgentHealthStatus.Unknown, AgentHealth.Evaluate(Heartbeat with { LastTickStartedUtc = now.AddSeconds(1) }, now));
        Assert.Equal(AgentHealthStatus.Unknown, AgentHealth.Evaluate(Heartbeat with { LastTickCompletedUtc = now.AddSeconds(1) }, now));
        Assert.Equal(AgentHealthStatus.Unknown, AgentHealth.Evaluate(Heartbeat, now, -1));
    }

    [Theory]
    [InlineData(0, AgentHealthStatus.Unknown)]
    [InlineData(120, AgentHealthStatus.Unknown)]
    [InlineData(121, AgentHealthStatus.Stale)]
    [InlineData(211, AgentHealthStatus.Down)]
    public void FirstTickCanBecomeStaleEvenIfItNeverCompletes(int age, AgentHealthStatus expected) =>
        Assert.Equal(expected, AgentHealth.Evaluate(Heartbeat with { LastTickStartedUtc = Tick, LastTickCompletedUtc = null }, Tick.AddSeconds(age)));

    [Fact]
    public void ConfiguredCadenceAndGraceDetermineHealth()
    {
        var heartbeat = Heartbeat with { TickIntervalSeconds = 180 };
        Assert.Equal(AgentHealthStatus.Healthy, AgentHealth.Evaluate(heartbeat, Tick.AddSeconds(420)));
        Assert.Equal(AgentHealthStatus.Stale, AgentHealth.Evaluate(heartbeat, Tick.AddSeconds(421)));
        Assert.Equal(AgentHealthStatus.Down, AgentHealth.Evaluate(heartbeat, Tick.AddSeconds(961)));
        Assert.Equal(AgentHealthStatus.Stale, AgentHealth.Evaluate(heartbeat, Tick.AddSeconds(361), 0));
    }

    [Theory]
    [InlineData(-1, 1, 0, null, ApplyState.Applied)]
    [InlineData(0, 1, 0, null, ApplyState.Applied)]
    [InlineData(1, 1, 0, null, ApplyState.Pending)]
    [InlineData(1, 121, 0, null, ApplyState.AgentDown)]
    [InlineData(1, 211, 0, null, ApplyState.AgentDown)]
    [InlineData(-1, 1, 1, "Router operation failed", ApplyState.Pending)]
    [InlineData(-1, 1, 0, "Rules evaluation failed", ApplyState.Pending)]
    [InlineData(-1, 211, 1, "Router operation failed", ApplyState.AgentDown)]
    [InlineData(-1, 211, 0, null, ApplyState.Applied)]
    [InlineData(10, 1, 0, null, ApplyState.Pending)]
    public void ApplyStatusUsesCompletionAndFailureEvidence(int saveOffset, int age, int failures, string? error, ApplyState expected)
    {
        var clock = new FakeClock(Tick.AddSeconds(age));
        Assert.Equal(expected, ApplyStatus.For(Tick.AddSeconds(saveOffset),
            Heartbeat with { ConsecutiveFailedTicks = failures, LastErrorSummary = error }, clock.GetUtcNow()));
    }

    [Fact]
    public void UnknownOrFutureHeartbeatLeavesSaveUnconfirmed()
    {
        Assert.Equal(ApplyState.Saved, ApplyStatus.For(Tick, null, Tick));
        Assert.Equal(ApplyState.Saved, ApplyStatus.For(Tick, Heartbeat with { LastTickCompletedUtc = null }, Tick));
        Assert.Equal(ApplyState.Saved, ApplyStatus.For(Tick, Heartbeat, Tick.AddSeconds(-2)));
        Assert.Equal(ApplyState.AgentDown, ApplyStatus.For(Tick, Heartbeat with { LastTickCompletedUtc = null }, Tick.AddSeconds(300)));
    }

    private sealed class FakeClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
