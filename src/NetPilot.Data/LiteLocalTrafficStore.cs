using LiteDB;
using NetPilot.Core.Usage;

namespace NetPilot.Data;

public sealed class LiteLocalTrafficStore(NetPilotDatabase db) : ILocalTrafficStore
{
    private readonly ILiteCollection<FlowDocument> _flows = db.GetCollection<FlowDocument>("lan_flow_state");
    private readonly ILiteCollection<StatusDocument> _status = db.GetCollection<StatusDocument>("lan_source_status");

    public Task<IReadOnlyList<LanFlowCounter>> GetFlowsAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<LanFlowCounter>>(_flows.FindAll().Select(f => new LanFlowCounter(f.SrcIp, f.DstIp, f.Bytes)).ToList());

    public Task SaveFlowsAsync(IReadOnlyList<LanFlowCounter> flows, CancellationToken ct)
    {
        foreach (var flow in flows)
            _flows.Upsert(new FlowDocument { Id = flow.Key, SrcIp = flow.SrcIp, DstIp = flow.DstIp, Bytes = flow.Bytes });
        return Task.CompletedTask;
    }

    public Task<LocalTrafficStatus> GetStatusAsync(CancellationToken ct) =>
        Task.FromResult(_status.FindById("source")?.Status ?? new LocalTrafficStatus());

    public Task SaveStatusAsync(LocalTrafficStatus status, CancellationToken ct)
    {
        db.InTransaction(() =>
        {
            var previous = _status.FindById("source")?.Status;
            _status.Upsert(new StatusDocument { Status = status with { LastWarningAtUtc = previous?.LastWarningAtUtc ?? status.LastWarningAtUtc } });
        });
        return Task.CompletedTask;
    }

    public Task<bool> TryRecordFailureAsync(DateTimeOffset now, CancellationToken ct)
    {
        var claimed = false;
        db.InTransaction(() =>
        {
            var previous = _status.FindById("source")?.Status ?? new LocalTrafficStatus();
            claimed = previous.LastWarningAtUtc is null || now - previous.LastWarningAtUtc >= TimeSpan.FromHours(1);
            _status.Upsert(new StatusDocument { Status = previous with { IsConfigured = true,
                UnavailableSinceUtc = previous.UnavailableSinceUtc ?? now,
                LastWarningAtUtc = claimed ? now : previous.LastWarningAtUtc } });
        });
        return Task.FromResult(claimed);
    }

    public sealed class FlowDocument
    {
        [BsonId] public string Id { get; set; } = "";
        public string SrcIp { get; set; } = "";
        public string DstIp { get; set; } = "";
        public long Bytes { get; set; }
    }

    public sealed class StatusDocument
    {
        [BsonId] public string Id { get; set; } = "source";
        public LocalTrafficStatus Status { get; set; } = new();
    }
}
