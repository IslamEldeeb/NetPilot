namespace NetPilot.Core.Usage;

public record LanFlowCounter(string SrcIp, string DstIp, long Bytes)
{
    public string Key => $"{SrcIp}|{DstIp}";
}

public record LocalTrafficReading(IReadOnlyList<LanFlowCounter> Flows, DateTimeOffset? GeneratedAtUtc);

/// <summary>Null means unconfigured. Configured sources throw on failure; tracking isolates those failures.</summary>
public interface ILocalTrafficSource
{
    Task<LocalTrafficReading?> ReadAsync(CancellationToken ct);
}

public sealed class NullLocalTrafficSource : ILocalTrafficSource
{
    public Task<LocalTrafficReading?> ReadAsync(CancellationToken ct) => Task.FromResult<LocalTrafficReading?>(null);
}
