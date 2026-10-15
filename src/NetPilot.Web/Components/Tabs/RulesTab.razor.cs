using System.Globalization;
using Microsoft.AspNetCore.Components;
using NetPilot.Core.Devices;
using NetPilot.Core.Enforcement.Claims;
using NetPilot.Core.Policy;
using NetPilot.Core.Rules;
using NetPilot.Core.Rules.Presentation;
using NetPilot.Core.Usage;

namespace NetPilot.Web.Components.Tabs;

public partial class RulesTab
{
    [Inject] private IRuleStore RuleStore { get; set; } = default!;
    [Inject] private IRuleFiringStore FiringStore { get; set; } = default!;
    [Inject] private IActionClaimStore ClaimStore { get; set; } = default!;
    [Inject] private IDeviceStore DeviceStore { get; set; } = default!;
    [Inject] private IPolicyStore PolicyStore { get; set; } = default!;
    [Inject] private UsagePeriodClock Clock { get; set; } = default!;
    [Inject] private RulesOptions Options { get; set; } = default!;
    [Inject] private IConfiguration Configuration { get; set; } = default!;
    [Inject] private ILogger<RulesTab> Logger { get; set; } = default!;
    private IReadOnlyList<Rule> _rules = [];
    private IReadOnlyList<RuleFiring> _firings = [];
    private IReadOnlyList<ActionClaim> _claims = [];
    private IReadOnlyList<Device> _devices = [];
    private IReadOnlyList<string> _categories = [];
    private Rule? _editing;
    private Guid _editorKey;
    private Guid? _deleteId;
    private string _hint = "";
    private bool _isNew, _busy, _loaded;
    private string? _error, _message;
    private string TickMinutes => (Configuration.GetValue("NetPilot:PollIntervalSeconds", 180) / 60m).ToString("0.##", CultureInfo.InvariantCulture);
    protected override Task OnInitializedAsync() => RefreshAsync();
    private string LocalTime(DateTimeOffset? utc) => utc.HasValue ? TimeZoneInfo.ConvertTime(utc.Value, Clock.TimeZone).ToString("yyyy-MM-dd HH:mm:ss") : "Never";

    private async Task LoadAsync()
    {
        _rules = await RuleStore.GetAllAsync(CancellationToken.None);
        _firings = await FiringStore.GetAllAsync(CancellationToken.None);
        _claims = await ClaimStore.GetAllAsync(CancellationToken.None);
        _devices = await DeviceStore.GetAllAsync(CancellationToken.None);
        _categories = (await PolicyStore.GetAllAsync(CancellationToken.None)).Select(p => p.CategoryKey)
            .Union(_devices.Select(d => d.CategoryKey)).Union(_rules.Where(r => r.Scope.Kind == RuleScopeKind.Category).Select(r => r.Scope.CategoryKey!))
            .Order().ToArray();
        _loaded = true;
    }

    private async Task RefreshAsync() => await RunAsync(LoadAsync);
    private void NewRule() => OpenEditor(new() { Actions = [new Notify("{rule}", "{device}: {used} of {cap} bytes")] }, true);
    private void StartTemplate(RuleTemplate template) => OpenEditor(template.Create(), true, template.Hint);
    private void Edit(Rule rule) => OpenEditor(rule, false);
    private void OpenEditor(Rule rule, bool isNew, string hint = "")
    {
        _editing = rule; _editorKey = Guid.NewGuid(); _isNew = isNew; _hint = hint;
    }
    private void CancelEditor() => _editing = null;
    private string NextTick => $"Takes effect on the Agent's next tick (about {TickMinutes} minutes).";

    private Task SaveAsync(Rule rule) => RunAsync(async () =>
    {
        var errors = rule.Validate();
        if (errors.Count > 0) { _error = string.Join(" ", errors); return; }
        if (!_isNew && await RuleStore.FindAsync(rule.Id, CancellationToken.None) is null)
        { _error = "This rule was deleted. Cancel and refresh before creating it again."; return; }
        await RuleStore.UpsertAsync(rule with { UpdatedAtUtc = DateTimeOffset.UtcNow }, CancellationToken.None);
        _editing = null;
        _message = "Rule saved. " + NextTick;
        await LoadAsync();
    });

    private Task ToggleAsync(Guid id, bool dryRun, bool value) => RunAsync(async () =>
    {
        var current = await RuleStore.FindAsync(id, CancellationToken.None);
        if (current is null) { _error = "This rule no longer exists. Refresh the list."; return; }
        var changed = dryRun ? current with { DryRun = value } : current with { Enabled = value };
        await RuleStore.UpsertAsync(changed with { UpdatedAtUtc = DateTimeOffset.UtcNow }, CancellationToken.None);
        _message = "Rule updated. " + NextTick;
        await LoadAsync();
    });

    private Task DeleteAsync(Guid id) => RunAsync(async () =>
    {
        await RuleStore.DeleteAsync(id, CancellationToken.None);
        if (_editing?.Id == id) _editing = null;
        _deleteId = null;
        _message = "Rule deleted. " + NextTick;
        await LoadAsync();
    });

    private Task RequestReleaseAsync(string id) => RunAsync(async () =>
    {
        var requested = await FiringStore.RequestReleaseAsync(id, CancellationToken.None);
        _message = requested ? $"Release requested – the Agent will release it on its next tick (about {TickMinutes} minutes). Other claims may still restrict this device."
            : "This firing is no longer active. Refresh to see its current status.";
        await LoadAsync();
    });

    private async Task RunAsync(Func<Task> action)
    {
        if (_busy) return;
        _busy = true; _error = _message = null;
        try { await action(); }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Rules dashboard operation failed");
            _error = "Could not complete the operation. Refresh to check saved state and try again.";
        }
        finally { _busy = false; }
    }
}
