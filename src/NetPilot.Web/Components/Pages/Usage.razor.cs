using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Components;
using NetPilot.Core.Budget;
using NetPilot.Core.Devices;
using NetPilot.Core.Usage;
using NetPilot.Web.Features;

namespace NetPilot.Web.Components.Pages;

public partial class Usage : IDisposable
{
    [SupplyParameterFromQuery(Name = "budget")] public bool EditBudget { get; set; }
    [SupplyParameterFromQuery(Name = "period")] public string? Period { get; set; }
    [SupplyParameterFromQuery(Name = "date")] public string? Date { get; set; }
    [SupplyParameterFromQuery(Name = "mac")] public string? DeviceMac { get; set; }
    private UsageOverview? _overview;
    private UsageReport? _report;
    private IReadOnlyList<DeviceUsageDay> _days = [];
    private ReportPeriod _period = ReportPeriod.Day;
    private DateTime? _date;
    private string _mac = "";
    private string? _error, _budgetError;
    private bool _editingBudget, _savingBudget;
    private decimal? _budgetGb;
    private int _billingDay = 1;
    private readonly CancellationTokenSource _lifetime = new();
    private IEnumerable<(string Mac, string Name)> DeviceChoices => _overview!.Readings.Devices
        .Select(device => (Mac: device.Mac.ToString(), Name: HouseholdUsage.DeviceName(device)))
        .Concat(_overview.Readings.States.Select(state => (Mac: state.Mac.ToString(), Name: state.Mac.ToString())))
        .Concat(_overview.Readings.Daily.Select(day => (Mac: day.Mac.ToString(), Name: day.Mac.ToString())))
        .Concat(_overview.Readings.Monthly.Select(month => (Mac: month.Mac.ToString(), Name: month.Mac.ToString())))
        .DistinctBy(choice => choice.Mac).OrderBy(choice => choice.Name);
    private string CsvHref => "data:text/csv;charset=utf-8;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(
        HouseholdUsage.Csv(_report!, Clock.TimeZone.Id, (_error is null ? "" : "Stale snapshot. ") + _overview!.Confidence)));

    protected override async Task OnParametersSetAsync()
    {
        _date = DateOnly.TryParseExact(Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) && parsed.Year is >= 2 and <= 9998 ? parsed.ToDateTime(TimeOnly.MinValue)
            : DateOnly.ParseExact(Clock.TodayKey(), "yyyy-MM-dd", CultureInfo.InvariantCulture).ToDateTime(TimeOnly.MinValue);
        _period = Enum.TryParse<ReportPeriod>(Period, true, out var period) && Enum.IsDefined(period) ? period : ReportPeriod.Day;
        _mac = MacAddress.TryParse(DeviceMac, out var mac) ? mac.ToString() : "";
        await LoadAsync();
        if (EditBudget) OpenBudget();
    }
    private async Task LoadAsync()
    {
        try { _overview = await Query.LoadAsync(_lifetime.Token); _error = null; Rebuild(); }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex) { Logger.LogWarning(ex, "Cannot load usage overview"); _error = "Could not load usage. Retry. Previously loaded totals may be stale."; }
    }
    private void Rebuild()
    {
        if (_overview is null || _date is null) return;
        var date = DateOnly.FromDateTime(_date.Value);
        _report = HouseholdUsage.Report(_overview.Readings, UsageRange.For(_period, date), _mac);
        _days = HouseholdUsage.Days(_overview.Readings, new(date.AddDays(-29), date.AddDays(1)), _mac);
    }
    private string LocalTime(DateTimeOffset? instant) => instant is { } timestamp ? $"{TimeZoneInfo.ConvertTime(timestamp, Clock.TimeZone):g} · {Clock.TimeZone.Id}" : "Unknown";
    private void ChangePeriod(ReportPeriod period) { _period = period; Rebuild(); }
    private void ChangeDate(DateTime? date) { if (date is null || date.Value.Year < 2 || date.Value.Year > 9998) return; _date = date; Rebuild(); }
    private void ChangeDevice(string mac) { _mac = mac; Rebuild(); }
    private void OpenBudget()
    {
        _budgetGb = _overview?.Budget is { } budget ? DataSize.ToGb(budget.MonthlyBytes) : null;
        _billingDay = _overview?.Budget?.BillingDay ?? 1;
        _budgetError = null;
        _editingBudget = true;
    }
    private async Task SaveBudgetAsync()
    {
        if (_budgetGb is null) { _budgetError = "Enter a household budget in decimal GB."; return; }
        try
        {
            _savingBudget = true;
            await Budgets.SaveAsync(new(DataSize.FromGb(_budgetGb.Value), _billingDay), _lifetime.Token);
            _editingBudget = false;
            await LoadAsync();
        }
        catch (ArgumentException ex) { _budgetError = ex.Message; }
        catch (Exception ex) { Logger.LogWarning(ex, "Cannot save budget"); _budgetError = "Could not save the budget. Retry."; }
        finally { _savingBudget = false; }
    }
    private async Task RemoveBudgetAsync()
    {
        try { _savingBudget = true; await Budgets.SaveAsync(null, _lifetime.Token); _editingBudget = false; await LoadAsync(); }
        catch (Exception ex) { Logger.LogWarning(ex, "Cannot remove budget"); _budgetError = "Could not remove the budget. Retry."; }
        finally { _savingBudget = false; }
    }
    public void Dispose() { _lifetime.Cancel(); _lifetime.Dispose(); }
}
