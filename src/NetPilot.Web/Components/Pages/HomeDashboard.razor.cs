using System.Globalization;
using Microsoft.AspNetCore.Components;
using NetPilot.Core.Dashboard;
using NetPilot.Core.Enforcement.Claims;
using NetPilot.Core.Health;
using NetPilot.Core.Quota;
using NetPilot.Core.RouterConnection;
using NetPilot.Core.Rules;
using NetPilot.Core.Usage;
using NetPilot.Web.Features;

namespace NetPilot.Web.Components.Pages;

public partial class HomeDashboard : IDisposable
{
    [Inject] private IQuotaStore Quotas { get; set; } = default!;
    [Inject] private IQuotaStateStore QuotaStates { get; set; } = default!;
    [Inject] private IProtectedDeviceStore Protected { get; set; } = default!;
    [Inject] private IActionClaimStore Claims { get; set; } = default!;
    [Inject] private IRuleFiringStore Firings { get; set; } = default!;
    [Inject] private IRuleStore Rules { get; set; } = default!;
    [Inject] private RulesOptions RulesOptions { get; set; } = default!;
    [Inject] private IRouterConnectionStore Connections { get; set; } = default!;
    private readonly CancellationTokenSource _lifetime = new();
    private UsageOverview? _overview;
    private UsageReport? _today;
    private IReadOnlyList<DashboardNotice> _attention = [], _restrictions = [];
    private string? _error;
    private string _quotaMode = "Unknown", _rulesMode = "Unknown", _temporaryMode = "Unknown";
    private bool _configured, _quickActions;
    private AgentHealthStatus Health => AgentHealth.Evaluate(_overview?.Heartbeat, Clock.GetUtcNow());
    private string RouterStatus => !_configured ? "Not configured" : _overview?.Heartbeat?.LastSuccessfulRouterReadUtc is null
        ? "Not observed" : Health != AgentHealthStatus.Healthy ? "Observation stale" : _overview.Heartbeat.ConsecutiveFailedTicks > 0 ? "Read or tick failed" : "Last read succeeded";

    protected override async Task OnInitializedAsync() { await LoadAsync(); _ = WatchAsync(); }
    private async Task LoadAsync()
    {
        try
        {
            var ct = _lifetime.Token;
            var overview = await UsageQuery.LoadAsync(ct);
            var policies = await Quotas.GetAllAsync(ct);
            var quotaStates = new List<QuotaPeriodState>();
            foreach (var device in overview.Readings.Devices)
                foreach (var kind in new[] { QuotaPeriodKind.Day, QuotaPeriodKind.Month })
                {
                    var key = kind == QuotaPeriodKind.Day ? Clock.TodayKey() : Clock.ThisMonthKey();
                    if (await QuotaStates.FindAsync(QuotaPeriodState.Key(device.Mac, kind, key), ct) is { } state) quotaStates.Add(state);
                }
            var claims = await Claims.GetAllAsync(ct);
            var snapshot = new DashboardInputs(overview.Readings, overview.Heartbeat, Clock.GetUtcNow(),
                await Protected.GetAllAsync(ct), claims, quotaStates, await Firings.GetAllAsync(ct));
            _attention = DashboardSummary.Attention(snapshot);
            _restrictions = DashboardSummary.Restrictions(snapshot, Clock);
            _configured = await Connections.GetAsync(ct) is not null;
            _quotaMode = Mode(policies.Where(policy => !policy.Exempt).Select(policy => policy.DryRun));
            _rulesMode = !RulesOptions.Enabled ? "Off" : RulesOptions.ForceDryRun ? "Forced dry-run" : Mode((await Rules.GetAllAsync(ct)).Where(rule => rule.Enabled).Select(rule => rule.DryRun));
            _temporaryMode = Mode(claims.Where(claim => claim.Source.StartsWith("temporary:", StringComparison.Ordinal)).Select(claim => claim.DryRun));
            _today = HouseholdUsage.Report(overview.Readings, UsageRange.For(ReportPeriod.Day, DateOnly.ParseExact(overview.Readings.Today, "yyyy-MM-dd", CultureInfo.InvariantCulture)));
            _overview = overview;
            _error = null;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex) { Logger.LogWarning(ex, "Cannot load home summary"); _error = "Could not load home summary. Retry. Previously loaded totals may be stale."; }
    }
    private static string Mode(IEnumerable<bool> dryRuns)
    {
        var modes = dryRuns.Distinct().ToArray();
        return modes.Length == 0 ? "Not set" : modes.Length > 1 ? "Mixed" : modes[0] ? "Dry-run" : "Enforcing";
    }
    private string LocalTime(DateTimeOffset? instant) => instant is { } timestamp ? $"{TimeZoneInfo.ConvertTime(timestamp, Clock.TimeZone):g} · {Clock.TimeZone.Id}" : "Unknown";
    private async Task WatchAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        try { while (await timer.WaitForNextTickAsync(_lifetime.Token)) await InvokeAsync(async () => { await LoadAsync(); StateHasChanged(); }); }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
    }
    public void Dispose() { _lifetime.Cancel(); _lifetime.Dispose(); }
}
