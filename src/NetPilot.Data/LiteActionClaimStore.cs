using LiteDB;
using NetPilot.Abstractions;
using NetPilot.Core.Devices;
using NetPilot.Core.Enforcement.Claims;
using NetPilot.Data.Documents;

namespace NetPilot.Data;

public class LiteActionClaimStore : IActionClaimStore
{
    private readonly ILiteCollection<ActionClaimDocument> _claims;
    private readonly ILiteCollection<ClaimDeviceStateDocument> _devices;

    public LiteActionClaimStore(NetPilotDatabase db)
    {
        _claims = db.GetCollection<ActionClaimDocument>("action_claims");
        _claims.EnsureIndex(c => c.Mac);
        _claims.EnsureIndex(c => c.Source);
        _devices = db.GetCollection<ClaimDeviceStateDocument>("action_claim_devices");
    }

    public Task<IReadOnlyList<ActionClaim>> GetAllAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ActionClaim>>(_claims.FindAll().Select(ToDomain).ToList());

    public Task<IReadOnlyList<ActionClaim>> GetForDeviceAsync(MacAddress mac, CancellationToken ct)
    {
        var macKey = mac.ToString();
        return Task.FromResult<IReadOnlyList<ActionClaim>>(_claims.Find(c => c.Mac == macKey).Select(ToDomain).ToList());
    }

    public Task UpsertAsync(ActionClaim claim, CancellationToken ct)
    {
        claim.Validate();
        _claims.Upsert(new ActionClaimDocument
        {
            Id = claim.Id, Mac = claim.Mac, Kind = claim.Kind, Source = claim.Source,
            ThrottleKbps = claim.ThrottleKbps, CreatedAtUtc = claim.CreatedAtUtc, Reason = claim.Reason
        });
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string id, CancellationToken ct)
    {
        _claims.Delete(id);
        return Task.CompletedTask;
    }

    public Task RemoveBySourceAsync(string source, CancellationToken ct)
    {
        _claims.DeleteMany(c => c.Source == source);
        return Task.CompletedTask;
    }

    public Task<ClaimDeviceState?> FindDeviceStateAsync(MacAddress mac, CancellationToken ct)
    {
        var doc = _devices.FindById((string)mac);
        return Task.FromResult(doc is null ? null : new ClaimDeviceState(new(doc.Mac),
            doc.PriorLimitEnabled is { } enabled ? new SpeedLimit(enabled, doc.PriorDownloadKbps, doc.PriorUploadKbps) : null,
            doc.BlockToken, doc.UnblockProjectionPending));
    }

    public Task UpsertDeviceStateAsync(ClaimDeviceState state, CancellationToken ct)
    {
        _devices.Upsert(new ClaimDeviceStateDocument
        {
            Mac = state.Mac, PriorLimitEnabled = state.PriorLimit?.Enabled,
            PriorDownloadKbps = state.PriorLimit?.DownloadKbps, PriorUploadKbps = state.PriorLimit?.UploadKbps,
            BlockToken = state.BlockToken, UnblockProjectionPending = state.UnblockProjectionPending
        });
        return Task.CompletedTask;
    }

    private static ActionClaim ToDomain(ActionClaimDocument doc)
    {
        var claim = new ActionClaim(new(doc.Mac), doc.Kind, doc.Source, doc.ThrottleKbps, doc.CreatedAtUtc, doc.Reason);
        claim.Validate();
        return claim;
    }
}
