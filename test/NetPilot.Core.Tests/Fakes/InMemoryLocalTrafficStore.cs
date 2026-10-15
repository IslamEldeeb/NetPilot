using NetPilot.Core.Usage;

namespace NetPilot.Core.Tests.Fakes;

public sealed class InMemoryLocalTrafficStore : ILocalTrafficStore
{
    public Dictionary<string, LanFlowCounter> Flows { get; } = [];
    public LocalTrafficStatus Status { get; private set; } = new();
    public Task<IReadOnlyList<LanFlowCounter>> GetFlowsAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<LanFlowCounter>>(Flows.Values.ToList());
    public Task SaveFlowsAsync(IReadOnlyList<LanFlowCounter> flows, CancellationToken ct)
    {
        foreach (var flow in flows) Flows[flow.Key] = flow;
        return Task.CompletedTask;
    }
    public Task<LocalTrafficStatus> GetStatusAsync(CancellationToken ct) => Task.FromResult(Status);
    public Task<bool> TryRecordFailureAsync(DateTimeOffset now, CancellationToken ct)
    {
        var warn = Status.LastWarningAtUtc is null || now - Status.LastWarningAtUtc >= TimeSpan.FromHours(1);
        Status = Status with { IsConfigured = true, UnavailableSinceUtc = Status.UnavailableSinceUtc ?? now,
            LastWarningAtUtc = warn ? now : Status.LastWarningAtUtc };
        return Task.FromResult(warn);
    }
    public Task SaveStatusAsync(LocalTrafficStatus status, CancellationToken ct)
    {
        Status = status with { LastWarningAtUtc = Status.LastWarningAtUtc ?? status.LastWarningAtUtc };
        return Task.CompletedTask;
    }
}

public sealed class FakeLocalTrafficSource : ILocalTrafficSource
{
    public LocalTrafficReading? Reading { get; set; }
    public Exception? Failure { get; set; }
    public Task<LocalTrafficReading?> ReadAsync(CancellationToken ct) => Failure is null
        ? Task.FromResult(Reading) : Task.FromException<LocalTrafficReading?>(Failure);
}
