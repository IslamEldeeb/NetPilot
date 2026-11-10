using Microsoft.AspNetCore.Components;
using MudBlazor;
using NetPilot.Core.Devices;
using NetPilot.Core.Enforcement.Claims;
using NetPilot.Core.Policy;
using NetPilot.Core.Quota;
using NetPilot.Core.RouterConnection;
using NetPilot.Core.Usage;
using NetPilot.Web.Components.DesignSystem;

namespace NetPilot.Web.Components.Pages;

public partial class Devices : IDisposable
{
    [Inject] private IDeviceStore DeviceStore { get; set; } = default!;
    [Inject] private IUsageStore UsageStore { get; set; } = default!;
    [Inject] private IQuotaStore QuotaStore { get; set; } = default!;
    [Inject] private IActionClaimStore ClaimStore { get; set; } = default!;
    [Inject] private IProtectedDeviceStore ProtectedStore { get; set; } = default!;
    [Inject] private IPolicyStore PolicyStore { get; set; } = default!;
    [Inject] private IRouterConnectionStore ConnectionStore { get; set; } = default!;
    [Inject] private DeviceRouterActions Actions { get; set; } = default!;
    [Inject] private UsagePeriodClock Clock { get; set; } = default!;
    [Inject] private Confirm Confirm { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private ILogger<Devices> Logger { get; set; } = default!;

    private bool _loaded, _refreshing, _bulkBusy, _bulkPause, _restrictedOnly;
    private string? _error;
    private string _search = "", _category = "", _connectionFilter = "all", _groupBy = "category";
    private DeviceSortKey _sort = DeviceSortKey.UsageTodayDesc;
    private RouterConnection? _connection;
    private IReadOnlyList<Device> _devices = [];
    private IReadOnlyList<DevicePolicy> _policies = [];
    private IReadOnlyList<QuotaPolicy> _quotas = [];
    private IReadOnlyList<DeviceListRow> _rows = [];
    private readonly HashSet<string> _selected = [];
    private DateTimeOffset? _lastRefresh;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _groupByCategory => _groupBy != "none";
    private IReadOnlyList<DeviceListRow> VisibleRows
    {
        get
        {
            var filtered = DeviceList.Apply(_rows,
                new(_search, _category, _connectionFilter == "online", _restrictedOnly, _sort, _groupByCategory));
            return _connectionFilter == "offline"
                ? filtered.Where(row => !row.Device.Connection.IsOnline).ToList()
                : filtered;
        }
    }
    private IEnumerable<string> Categories => _devices.Select(d => d.CategoryKey).Distinct().Order(StringComparer.OrdinalIgnoreCase);
    private string CountText => $"{DeviceList.Counts(_rows).Total} known devices · {DeviceList.Counts(_rows).Online} online now";

    protected override async Task OnInitializedAsync() { await LoadAsync(); _ = WatchAsync(); }
    private async Task WatchAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        try
        {
            while (await timer.WaitForNextTickAsync(_lifetime.Token))
                await InvokeAsync(async () => { await LoadAsync(); StateHasChanged(); });
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
    }

    private async Task LoadAsync()
    {
        try
        {
            var ct = _lifetime.Token;
            _connection = await ConnectionStore.GetAsync(ct);
            _devices = await DeviceStore.GetAllAsync(ct);
            var states = await UsageStore.GetAllStatesAsync(ct);
            var monthly = await UsageStore.GetAllHistoryAsync(ct);
            var daily = await UsageStore.GetAllDailyHistoryAsync(ct);
            var today = UsageQuery.BytesByDevice(UsagePeriodType.Day, Clock.TodayKey(), true, states, monthly, daily);
            var month = UsageQuery.BytesByDevice(UsagePeriodType.Month, Clock.ThisMonthKey(), true, states, monthly, daily);
            _quotas = await QuotaStore.GetAllAsync(ct);
            _policies = await PolicyStore.GetAllAsync(ct);
            _rows = DeviceList.Build(new(_devices, today, month, _quotas, await ClaimStore.GetAllAsync(ct),
                await ProtectedStore.GetAllAsync(ct), Clock.GetUtcNow()));
            _selected.IntersectWith(_devices.Select(d => (string)d.Mac));
            _error = null;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex) { Logger.LogWarning(ex, "Cannot load devices"); _error = "Could not load devices. Retry or open Classic view."; }
        finally { _loaded = true; }
    }

    private async Task RefreshFromRouterAsync()
    {
        _refreshing = true;
        try { await Actions.RefreshAsync(); _lastRefresh = Clock.GetUtcNow(); await LoadAsync(); }
        catch (Exception ex) { Logger.LogWarning(ex, "Manual device refresh failed"); _error = "Refresh failed. Check the router connection in Classic view and retry."; }
        finally { _refreshing = false; }
    }

    private async Task BulkSetBlockAsync(bool block)
    {
        var targets = _rows.Where(r => _selected.Contains((string)r.Device.Mac)).ToArray();
        if (targets.Length == 0) return;
        var message = $"{(block ? "Block" : "Unblock")} {targets.Length} selected device(s)? Manual blocks apply even to protected devices; protection only guards automatic quota and rule actions. Other claims can still restrict a device after unblocking.";
        if (!await Confirm.DangerAsync(block ? "Block selected devices" : "Unblock selected devices", message, block ? "Block" : "Unblock")) return;
        _bulkBusy = true;
        foreach (var row in targets)
        {
            try
            {
                if (block) await Actions.BlockAsync(row.Device.Mac);
                else await Actions.UnblockAsync(row.Device.Mac);
                _selected.Remove(row.Device.Mac);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Bulk device action failed for {Mac}", row.Device.Mac);
                Snackbar.Add($"{row.DisplayName}: action failed. Refresh and retry; the router may already have accepted it.", Severity.Error);
            }
        }
        _bulkBusy = false;
        await LoadAsync();
    }

    private void ToggleSelect(string mac, bool selected) { if (selected) _selected.Add(mac); else _selected.Remove(mac); }
    private void ClearFilters() { _search = ""; _category = ""; _connectionFilter = "all"; _restrictedOnly = false; }
    public void Dispose() { _lifetime.Cancel(); _lifetime.Dispose(); }
}
