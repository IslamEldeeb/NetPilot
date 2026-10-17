using Microsoft.AspNetCore.Components;
using NetPilot.Core.Devices;
using NetPilot.Core.Enforcement;
using NetPilot.Core.Enforcement.Claims;
using NetPilot.Core.Health;
using NetPilot.Core.Policy;
using NetPilot.Core.Quota;
using NetPilot.Core.RouterConnection;
using NetPilot.Core.Rules;
using NetPilot.Core.Usage;
using NetPilot.Web.Components.DesignSystem;

namespace NetPilot.Web.Components.Pages;

public partial class DevicePage : IDisposable
{
    [Parameter] public string Mac { get; set; } = "";
    [Inject] private IDeviceStore Devices { get; set; } = default!;
    [Inject] private IUsageStore Usage { get; set; } = default!;
    [Inject] private IQuotaStore Quotas { get; set; } = default!;
    [Inject] private IQuotaStateStore QuotaStates { get; set; } = default!;
    [Inject] private IActionClaimStore Claims { get; set; } = default!;
    [Inject] private IRuleStore Rules { get; set; } = default!;
    [Inject] private IRuleFiringStore Firings { get; set; } = default!;
    [Inject] private IProtectedDeviceStore Protected { get; set; } = default!;
    [Inject] private IPolicyStore Policies { get; set; } = default!;
    [Inject] private IActivityLogStore Activity { get; set; } = default!;
    [Inject] private IAgentHeartbeatStore Heartbeats { get; set; } = default!;
    [Inject] private IRouterConnectionStore Connections { get; set; } = default!;
    [Inject] private UsagePeriodClock Clock { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Inject] private Confirm Confirm { get; set; } = default!;
    [Inject] private ILogger<DevicePage> Logger { get; set; } = default!;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Dictionary<string, DateTimeOffset> _saves = [];
    private Device? _device;
    private MacAddress _mac;
    private bool _quickActions;
    private bool _valid, _loaded, _busy, _editing, _protected, _hasRouter, _month;
    private string? _error;
    private string _name = "", _category = "", _icon = "";
    private long _todayBytes, _monthBytes, _todayLan, _monthLan;
    private IReadOnlyList<DeviceUsageDay> _days = [];
    private IReadOnlyList<DevicePolicy> _policies = [];
    private IReadOnlyList<Rule> _rules = [];
    private IReadOnlyList<ActionClaim> _claims = [];
    private IReadOnlyList<QuotaPeriodState> _quotaStates = [];
    private IReadOnlyList<RuleFiring> _firings = [];
    private IReadOnlyList<ActivityLogEntry> _activity = [];
    private QuotaResolution _quota = new(QuotaResolutionKind.NotConfigured, null);
    private AgentHeartbeat? _heartbeat;
    private DeviceListRow? _row;

    protected override void OnInitialized() => _ = WatchAsync();
    protected override async Task OnParametersSetAsync()
    {
        _loaded = false;
        _device = null;
        _error = null;
        _editing = false;
        _valid = MacAddress.TryParse(Mac, out _mac);
        if (!_valid) { _loaded = true; return; }
        if (Mac != _mac.ToString()) { Navigation.NavigateTo($"/devices/{_mac}", replace: true); return; }
        _saves.Clear();
        await LoadAsync();
    }

    private async Task WatchAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        try
        {
            while (await timer.WaitForNextTickAsync(_lifetime.Token))
                await InvokeAsync(async () => { if (_valid) await LoadAsync(); StateHasChanged(); });
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
    }

    private async Task LoadAsync()
    {
        try
        {
            var ct = _lifetime.Token;
            _device = await Devices.FindByMacAsync(_mac, ct);
            if (_device is null) return;
            _hasRouter = await Connections.GetAsync(ct) is not null;
            _heartbeat = await Heartbeats.GetAsync(ct);
            _protected = await Protected.IsProtectedAsync(_mac, ct);
            _policies = await Policies.GetAllAsync(ct);
            var quotaPolicies = await Quotas.GetAllAsync(ct);
            _quota = QuotaResolver.Resolve(_device, quotaPolicies);
            _claims = await Claims.GetForDeviceAsync(_mac, ct);
            _rules = await Rules.GetAllAsync(ct);
            _quotaStates = (await QuotaStates.ListWithActiveActionAsync(ct)).Where(s => s.Mac == _mac).ToArray();
            _firings = (await Firings.GetAllAsync(ct)).Where(MentionsDevice).OrderByDescending(f => f.FiredAtUtc).ToArray();
            _activity = (await Activity.GetRecentAsync(500, ct)).Where(e => e.Mac == _mac).Take(20).ToArray();
            await LoadUsageAsync(ct);
            _row = DeviceList.Build(new([_device], new Dictionary<string, long> { [_mac] = _todayBytes },
                new Dictionary<string, long> { [_mac] = _monthBytes }, quotaPolicies, _claims,
                _protected ? [_mac] : [], Clock.GetUtcNow()))[0];
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex) { Logger.LogWarning(ex, "Cannot load device {Mac}", _mac); _error = "Could not load device details. Retry or open Classic view."; }
        finally { _loaded = true; }
    }

    private async Task LoadUsageAsync(CancellationToken ct)
    {
        var states = await Usage.GetAllStatesAsync(ct);
        var monthly = await Usage.GetAllHistoryAsync(ct);
        var daily = await Usage.GetAllDailyHistoryAsync(ct);
        _todayBytes = UsageQuery.BytesByDevice(UsagePeriodType.Day, Clock.TodayKey(), true, states, monthly, daily).GetValueOrDefault(_mac);
        _monthBytes = UsageQuery.BytesByDevice(UsagePeriodType.Month, Clock.ThisMonthKey(), true, states, monthly, daily).GetValueOrDefault(_mac);
        _todayLan = UsageQuery.LanBytesByDevice(UsagePeriodType.Day, Clock.TodayKey(), true, states, monthly, daily).GetValueOrDefault(_mac);
        _monthLan = UsageQuery.LanBytesByDevice(UsagePeriodType.Month, Clock.ThisMonthKey(), true, states, monthly, daily).GetValueOrDefault(_mac);
        _days = DeviceUsageHistory.Last30Days(_mac, Clock.TodayKey(), states.FirstOrDefault(s => s.Mac == _mac), daily);
    }

    private bool MentionsDevice(RuleFiring firing) => firing.TargetMacs.Contains(_mac) || firing.Subject == _mac.ToString()
        || firing.AppliedClaimIds.Any(id => _claims.Any(c => c.Id == id));
    private string LocalTime(DateTimeOffset instant) => TimeZoneInfo.ConvertTime(instant, Clock.TimeZone).ToString("g");
    private ApplyState Status(string key, DateTimeOffset saved) => ApplyStatus.For(_saves.GetValueOrDefault(key, saved), _heartbeat, Clock.GetUtcNow());
    private ApplyState ReleaseStatus(string key, DateTimeOffset saved, bool requested)
    {
        if (!requested || _saves.ContainsKey(key)) return Status(key, saved);
        return AgentHealth.Evaluate(_heartbeat, Clock.GetUtcNow()) switch
        {
            AgentHealthStatus.Unknown => ApplyState.Saved,
            AgentHealthStatus.Down or AgentHealthStatus.Stale => ApplyState.AgentDown,
            _ => ApplyState.Pending
        };
    }
    private void BeginEdit()
    {
        _name = _device!.FriendlyName ?? "";
        _category = _device.LocalCategoryKey ?? "";
        _icon = _device.LocalIconKey ?? "";
        _editing = true;
    }
    private Task SaveIdentityAsync() => ExecuteAsync("identity", async () =>
    {
        if (_name.Trim().Length > 80 || _category.Trim().Length > 80) throw new InvalidOperationException("Names and categories must be 80 characters or fewer.");
        await Devices.UpdateIdentityAsync(_mac, new(NullIfBlank(_name), NullIfBlank(_category), NullIfBlank(_icon)), _lifetime.Token);
        _editing = false;
    });
    private async Task ClearIdentityAsync()
    {
        if (!await Confirm.DangerAsync("Clear identity overrides?", "Restore the router hostname, router category and default category icon.", "Clear overrides")) return;
        await ExecuteAsync("identity", async () => { await Devices.UpdateIdentityAsync(_mac, new(null, null, null), _lifetime.Token); _editing = false; });
    }
    private static string? NullIfBlank(string text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private async Task ChangeProtectionAsync(bool enabled)
    {
        if (!await Confirm.DangerAsync(enabled ? "Protect device?" : "Remove protection?",
            "Protected devices will be exempt from automatic Throttle and Block actions. Usage tracking and notifications still apply. Manual blocks and wireless changes can still affect this device.", enabled ? "Protect" : "Remove protection")) return;
        await ExecuteAsync("protection", () => enabled ? Protected.AddAsync(_mac, _lifetime.Token) : Protected.RemoveAsync(_mac, _lifetime.Token));
    }
    private async Task ChangeLanOnlyAsync(bool enabled)
    {
        if (!await Confirm.DangerAsync(enabled ? "Mark LAN-only?" : "Count Internet traffic again?",
            "LAN-only devices count all future traffic as LAN. Remote viewers and other Internet traffic from these devices will also be hidden. Existing totals stay unchanged. The TV’s traffic will not change.", "Save")) return;
        await ExecuteAsync("lan", async () =>
        {
            var saved = await Devices.FindByMacAsync(_mac, _lifetime.Token) ?? throw new InvalidOperationException("Device no longer exists.");
            saved.UsageLanOnly = enabled;
            await Devices.UpsertAsync(saved, _lifetime.Token);
        });
    }
    private async Task ReleaseQuotaAsync(QuotaPeriodState state)
    {
        if (!await Confirm.DangerAsync("Release this quota claim?", "Suppress this quota action for its named period on the next Agent tick. Other daily, monthly and rule claims can remain.", "Request release")) return;
        await ExecuteAsync(state.Id, async () =>
        {
            var updated = await QuotaStates.UpdateAsync(state.Id, current => current is { ActionKind: not QuotaActionKind.None }
                ? current with { ReleaseRequested = true } : null, _lifetime.Token);
            if (updated is null) throw new InvalidOperationException("This quota action is no longer active.");
        });
    }
    private async Task ReleaseRuleAsync(RuleFiring firing)
    {
        if (!await Confirm.DangerAsync("Release this rule firing?", "The Agent will release this firing on its next tick. Other claims may remain; usage rules can fire again after their cooldown or once-per period.", "Request release")) return;
        await ExecuteAsync(firing.Id, async () =>
        {
            if (!await Firings.RequestReleaseAsync(firing.Id, _lifetime.Token)) throw new InvalidOperationException("This rule firing is no longer active.");
        });
    }
    private async Task ExecuteAsync(string key, Func<Task> action)
    {
        _busy = true;
        _error = null;
        try { await action(); _saves[key] = Clock.GetUtcNow(); await LoadAsync(); }
        catch (Exception ex) { Logger.LogWarning(ex, "Device change failed for {Mac}", _mac); _error = "Could not save this change. Retry; your identity draft is preserved."; }
        finally { _busy = false; }
    }
    public void Dispose() { _lifetime.Cancel(); _lifetime.Dispose(); }
}
