using LiteDB;
using NetPilot.Core.Health;

namespace NetPilot.Data;

public sealed class LiteAgentHeartbeatStore(NetPilotDatabase db) : IAgentHeartbeatStore
{
    private readonly ILiteCollection<HeartbeatDocument> _collection = db.GetCollection<HeartbeatDocument>("agent_heartbeat");

    public Task<AgentHeartbeat?> GetAsync(CancellationToken ct) =>
        Task.FromResult(_collection.FindById("agent")?.Heartbeat);

    public Task SaveAsync(AgentHeartbeat heartbeat, CancellationToken ct)
    {
        _collection.Upsert(new HeartbeatDocument { Heartbeat = heartbeat });
        return Task.CompletedTask;
    }

    public sealed class HeartbeatDocument
    {
        [BsonId] public string Id { get; set; } = "agent";
        public AgentHeartbeat Heartbeat { get; set; } = new();
    }
}
