using NetPilot.Core.Devices;
using NetPilot.Core.Health;
using NetPilot.Core.Quota;
using NetPilot.Core.Rules;
using NetPilot.Core.Safety;
using NetPilot.Core.Usage;

namespace NetPilot.Data.Tests;

public sealed class SafetyGraduationTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"netpilot-safety-{Guid.NewGuid():N}");
    private static readonly CancellationToken Ct = default;
    public SafetyGraduationTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public async Task ConfirmationEvidenceAndVersionSpecificRehearsalAreRequired_OnlySelectedPolicyChanges()
    {
        using var db = new NetPilotDatabase(Path.Combine(_directory, "test.db"));
        var (service, store, quotas, _) = await ArrangeAsync(db);
        var policy = (await service.GetPoliciesAsync(Ct)).Single(p => p.Id == "default");
        Assert.Contains("confirmation", await service.GraduateAsync(policy, "Admin", false, Ct));
        Assert.Contains("every evidence", await service.GraduateAsync(policy, "Admin", true, Ct));
        foreach (var id in SafetyGraduationService.RequiredEvidence) await store.SaveEvidenceAsync(new(id, "Admin", "Measured manually on home network", DateTimeOffset.UtcNow), Ct);
        Assert.Contains("rehearsal", await service.GraduateAsync(policy, "Admin", true, Ct));
        await store.SaveEvidenceAsync(new(policy.RehearsalId, "Admin", "Test phone throttle released; router confirmed recovery", DateTimeOffset.UtcNow), Ct);
        Assert.Null(await service.GraduateAsync(policy, "Admin", true, Ct));
        Assert.False((await quotas.FindAsync("default", Ct))!.DryRun);
        Assert.True((await quotas.FindAsync("cat:mobile", Ct))!.DryRun);
        var record = Assert.Single(await store.GetGraduationsAsync(Ct)); Assert.Equal(policy.Fingerprint, record.Policy.Fingerprint); Assert.Equal(7, record.Evidence.Length);
        Assert.NotNull(await service.GraduateAsync(policy, "Admin", true, Ct)); Assert.Single(await store.GetGraduationsAsync(Ct));
    }

    [Fact]
    public async Task ChangedPolicyRejectsReviewedVersion_AndOldRehearsalDoesNotAuthorizeNewVersion()
    {
        using var db = new NetPilotDatabase(Path.Combine(_directory, "test.db"));
        var (service, store, quotas, _) = await ArrangeAsync(db);
        var reviewed = (await service.GetPoliciesAsync(Ct)).Single(p => p.Id == "default");
        foreach (var id in SafetyGraduationService.RequiredEvidence.Append(reviewed.RehearsalId)) await store.SaveEvidenceAsync(new(id, "Admin", "Observed", DateTimeOffset.UtcNow), Ct);
        var quota = (await quotas.FindAsync("default", Ct))!; await quotas.UpsertAsync(quota with { DailyBytes = 10_000_000_000 }, Ct);
        Assert.Contains("changed", await service.GraduateAsync(reviewed, "Admin", true, Ct));
        var updated = (await service.GetPoliciesAsync(Ct)).Single(p => p.Id == "default");
        Assert.Contains("rehearsal", await service.GraduateAsync(updated, "Admin", true, Ct));
        Assert.False(await store.TryGraduateAsync(new(Guid.NewGuid().ToString(), reviewed, "Admin", DateTimeOffset.UtcNow, []), Ct));
        Assert.Empty(await store.GetGraduationsAsync(Ct)); Assert.True((await quotas.FindAsync("default", Ct))!.DryRun);
    }

    [Fact]
    public async Task SameCountDifferentTargetsRequiresFreshReview()
    {
        using var db = new NetPilotDatabase(Path.Combine(_directory, "test.db"));
        var (service, store, _, _) = await ArrangeAsync(db);
        var review = (await service.GetPoliciesAsync(Ct)).Single(p => p.Id == "default");
        foreach (var key in SafetyGraduationService.RequiredEvidence.Append(review.RehearsalId)) await store.SaveEvidenceAsync(new(key, "Admin", "Observed", DateTimeOffset.UtcNow), Ct);
        var devices = new LiteDeviceStore(db);
        await devices.UpsertAsync(new() { Mac = new("AA-BB-CC-DD-EE-01"), CategoryKey = "mobile" }, Ct);
        await devices.UpsertAsync(new() { Mac = new("AA-BB-CC-DD-EE-02"), CategoryKey = "television" }, Ct);
        var current = (await service.GetPoliciesAsync(Ct)).Single(p => p.Id == "default");
        Assert.Equal(review.Targets, current.Targets);
        Assert.Contains("changed", await service.GraduateAsync(review, "Admin", true, Ct));
        Assert.Empty(await store.GetGraduationsAsync(Ct));
    }

    [Theory]
    [InlineData(true, false)] [InlineData(false, true)]
    public async Task ForcedModeOnEitherHostBlocksRuleGraduation(bool webForce, bool agentForce)
    {
        using var db = new NetPilotDatabase(Path.Combine(_directory, "test.db"));
        var (service, store, _, rules) = await ArrangeAsync(db, webForce, agentForce);
        var rule = new Rule { Name = "Reviewed rule", Actions = [new Throttle(512)] }; await rules.UpsertAsync(rule, Ct);
        var reviewed = (await service.GetPoliciesAsync(Ct)).Single(p => p.Kind == SafetyPolicyKind.Rule);
        foreach (var id in SafetyGraduationService.RequiredEvidence.Append(reviewed.RehearsalId)) await store.SaveEvidenceAsync(new(id, "Admin", "Observed", DateTimeOffset.UtcNow), Ct);
        Assert.Contains("forced dry-run", await service.GraduateAsync(reviewed, "Admin", true, Ct));
        Assert.True((await rules.FindAsync(rule.Id, Ct))!.DryRun); Assert.Empty(await store.GetGraduationsAsync(Ct));
    }

    [Fact]
    public async Task AllowedRuleGraduationPersistsRecordAndRuleAtomicallyAcrossReopen()
    {
        var path = Path.Combine(_directory, "test.db"); var id = Guid.NewGuid();
        using (var db = new NetPilotDatabase(path))
        {
            var (service, store, _, rules) = await ArrangeAsync(db);
            await rules.UpsertAsync(new Rule { Id = id, Name = "Bedtime", Actions = [new Throttle(512)], Scope = new() { Kind = RuleScopeKind.Devices, Devices = [new("AA-BB-CC-DD-EE-01")] } }, Ct);
            var review = (await service.GetPoliciesAsync(Ct)).Single(p => p.Kind == SafetyPolicyKind.Rule);
            foreach (var key in SafetyGraduationService.RequiredEvidence.Append(review.RehearsalId)) await store.SaveEvidenceAsync(new(key, "Admin", "Measured manually", DateTimeOffset.UtcNow), Ct);
            Assert.Null(await service.GraduateAsync(review, "Admin", true, Ct));
        }
        using var reopened = new NetPilotDatabase(path);
        var saved = (await new LiteRuleStore(reopened).FindAsync(id, Ct))!;
        Assert.False(saved.DryRun); Assert.IsType<Throttle>(Assert.Single(saved.Actions)); Assert.Single(saved.Scope.Devices);
        Assert.Single(await new LiteSafetyStore(reopened).GetGraduationsAsync(Ct));
    }

    private static async Task<(SafetyGraduationService, LiteSafetyStore, LiteQuotaStore, LiteRuleStore)> ArrangeAsync(NetPilotDatabase db, bool webForce = false, bool agentForce = false)
    {
        var clock = new UsagePeriodClock(); var now = clock.GetUtcNow();
        var heartbeats = new LiteAgentHeartbeatStore(db);
        await heartbeats.SaveAsync(new() { LastTickStartedUtc = now.AddSeconds(-10), LastTickCompletedUtc = now.AddSeconds(-5), LastSuccessfulRouterReadUtc = now.AddSeconds(-6), TickIntervalSeconds = 180, EffectiveModes = new(agentForce, true) }, Ct);
        var quotas = new LiteQuotaStore(db); var rules = new LiteRuleStore(db); var store = new LiteSafetyStore(db); var devices = new LiteDeviceStore(db);
        await quotas.UpsertAsync(new() { Id = "default", DailyBytes = 5_000_000_000, Action = QuotaAction.Throttle, ThrottleKbps = 512 }, Ct);
        await quotas.UpsertAsync(new() { Id = "cat:mobile", Scope = QuotaScope.Category, CategoryKey = "mobile", DailyBytes = 2_000_000_000 }, Ct);
        await devices.UpsertAsync(new() { Mac = new("AA-BB-CC-DD-EE-01"), CategoryKey = "television" }, Ct);
        return (new(store, quotas, rules, devices, heartbeats, clock, new() { ForceDryRun = webForce }), store, quotas, rules);
    }
    public void Dispose() => Directory.Delete(_directory, true);
}
