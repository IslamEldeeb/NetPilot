using System.Globalization;
using LiteDB;
using NetPilot.Abstractions;
using NetPilot.Core.Quota;
using NetPilot.Data.Documents;

namespace NetPilot.Data;

public class LiteQuotaStateStore(NetPilotDatabase db) : IQuotaStateStore
{
    private readonly ILiteCollection<QuotaPeriodStateDocument> _collection = db.GetCollection<QuotaPeriodStateDocument>("quota_period_state");

    public Task<QuotaPeriodState?> FindAsync(string id, CancellationToken ct)
    {
        var doc = _collection.FindById(id);
        return Task.FromResult(doc is null ? null : ToDomain(doc));
    }

    public Task UpsertAsync(QuotaPeriodState state, CancellationToken ct)
    {
        _collection.Upsert(ToDocument(state));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<QuotaPeriodState>> ListWithActiveActionAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<QuotaPeriodState>>(_collection.Find(d => d.ActionKind != QuotaActionKind.None).Select(ToDomain).ToList());

    public Task PruneAsync(DateTimeOffset olderThanUtc, CancellationToken ct)
    {
        var cutoff = olderThanUtc.UtcDateTime.Date;
        _collection.DeleteMany(d => d.ActionKind == QuotaActionKind.None && d.PeriodDateUtc < cutoff);
        return Task.CompletedTask;
    }

    private static QuotaPeriodState ToDomain(QuotaPeriodStateDocument doc) => new()
    {
        Mac = new(doc.Mac), PeriodKind = doc.PeriodKind, PeriodKey = doc.PeriodKey,
        WarnedPercents = doc.WarnedPercents, ExceededNotified = doc.ExceededNotified,
        ActionKind = doc.ActionKind, ActionAppliedAtUtc = doc.ActionAppliedAtUtc,
        ExtraAllowanceBytes = doc.ExtraAllowanceBytes,
        PriorLimit = doc.PriorLimitEnabled is null ? null : new SpeedLimit(doc.PriorLimitEnabled.Value, doc.PriorDownloadKbps, doc.PriorUploadKbps),
        BlockToken = doc.BlockToken, AppliedThrottleKbps = doc.AppliedThrottleKbps, DryRunWouldHaveActed = doc.DryRunWouldHaveActed,
        ActionSkippedProtected = doc.ActionSkippedProtected, CapBytes = doc.CapBytes, UsedBytes = doc.UsedBytes
    };

    private static QuotaPeriodStateDocument ToDocument(QuotaPeriodState state) => new()
    {
        Id = state.Id, Mac = state.Mac, PeriodKind = state.PeriodKind, PeriodKey = state.PeriodKey,
        PeriodDateUtc = DateTime.ParseExact(state.PeriodKey, state.PeriodKind == QuotaPeriodKind.Day ? "yyyy-MM-dd" : "yyyy-MM",
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal),
        WarnedPercents = state.WarnedPercents, ExceededNotified = state.ExceededNotified,
        ActionKind = state.ActionKind, ActionAppliedAtUtc = state.ActionAppliedAtUtc,
        ExtraAllowanceBytes = state.ExtraAllowanceBytes, PriorLimitEnabled = state.PriorLimit?.Enabled,
        PriorDownloadKbps = state.PriorLimit?.DownloadKbps, PriorUploadKbps = state.PriorLimit?.UploadKbps,
        BlockToken = state.BlockToken, AppliedThrottleKbps = state.AppliedThrottleKbps, DryRunWouldHaveActed = state.DryRunWouldHaveActed,
        ActionSkippedProtected = state.ActionSkippedProtected, CapBytes = state.CapBytes, UsedBytes = state.UsedBytes
    };
}
