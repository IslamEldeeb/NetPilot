using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetPilot.Core.Budget;
using NetPilot.Core.Devices;
using NetPilot.Core.Enforcement.Claims;
using NetPilot.Core.Health;
using NetPilot.Core.Quota;
using NetPilot.Core.Usage;
using NetPilot.Web.Components.Pages;

namespace NetPilot.Data.Tests.Web;

public sealed class BatchCRenderingTests
{
    [Theory]
    [InlineData(typeof(HomeDashboard))]
    [InlineData(typeof(Usage))]
    public async Task SavedSummary_RendersSameTotals_BudgetEstimateAndDeepLinks(Type page)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"netpilot-batch-c-render-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            await using var services = ShellRenderingTests.Services(directory);
            var clock = services.GetRequiredService<UsagePeriodClock>();
            var now = clock.GetUtcNow();
            var today = DateOnly.ParseExact(clock.TodayKey(), "yyyy-MM-dd");
            var mac = new MacAddress("AA-BB-CC-DD-EE-01");
            await services.GetRequiredService<IDeviceStore>().UpsertAsync(new() { Mac = mac, FriendlyName = "Bedroom TV", CategoryKey = "television", FirstSeenAtUtc = now.AddHours(-1) }, default);
            await services.GetRequiredService<IUsageStore>().UpsertStateAsync(new() { Mac = mac, CurrentDayKey = clock.TodayKey(), CurrentDayBytes = 5_000_000_000,
                CurrentDayLanBytes = 2_000_000_000, CurrentMonthKey = clock.ThisMonthKey(), CurrentMonthBytes = 35_000_000_000 }, default);
            for (var offset = 1; offset <= 3; offset++)
            {
                var day = today.AddDays(-offset);
                await services.GetRequiredService<IUsageStore>().AppendDailyHistoryAsync(new(mac, day.ToString("yyyy-MM-dd"), 10_000_000_000,
                    now.AddDays(1 - offset), ForecastEligible: true), default);
            }
            await services.GetRequiredService<IHouseholdBudgetStore>().SaveAsync(new(300_000_000_000, today.AddDays(-3).Day), default);
            await services.GetRequiredService<IAgentHeartbeatStore>().SaveAsync(new() { LastTickStartedUtc = now.AddSeconds(-5), LastTickCompletedUtc = now,
                LastSuccessfulRouterReadUtc = now, TickIntervalSeconds = 180 }, default);
            await services.GetRequiredService<IProtectedDeviceStore>().AddAsync(mac, default);
            await services.GetRequiredService<IActionClaimStore>().UpsertAsync(new(mac, ClaimKind.Throttle, "rule:bedtime", 128, now), default);
            var html = await RenderAsync(services, page);
            Assert.Contains("5 GB", html);
            Assert.Contains("2 GB", html);
            Assert.Contains("Bedroom TV", html);
            Assert.Contains("/devices/AA-BB-CC-DD-EE-01", html);
            Assert.Contains("Household ISP budget", html);
            Assert.Contains("Estimated: about", html);
            Assert.Contains("Divide remaining cycle budget", html);
            Assert.DoesNotContain("Could not load", html);
            if (page == typeof(Usage))
            {
                Assert.Contains("Export shown period CSV", html);
                Assert.Contains("View exact chart values", html);
                Assert.Contains("5000000000", html.Replace(",", ""));
                Assert.Contains("Category ranking", html);
            }
            else
            {
                Assert.Contains("protected from automatic restrictions", html);
                Assert.Contains("newly discovered", html);
                Assert.Contains("rule:bedtime", html);
                Assert.Contains("Quick actions", html);
            }
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(typeof(HomeDashboard), "Unknown · no observation for today")]
    [InlineData(typeof(Usage), "No saved usage for this period")]
    public async Task EmptyStores_NeverShowUnknownUsageAsZeroOrInventBudget(Type page, string expected)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"netpilot-batch-c-empty-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            await using var services = ShellRenderingTests.Services(directory);
            var html = await RenderAsync(services, page);
            Assert.Contains(expected, html);
            Assert.Contains("No household budget configured", html);
            Assert.DoesNotContain("0 of 0", html);
            Assert.DoesNotContain("Estimated: about", html);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(typeof(HomeDashboard), "Loading home summary", "Could not load home summary")]
    [InlineData(typeof(Usage), "Loading usage", "Could not load usage")]
    public async Task PendingAndFailedRead_RenderLoadingThenActionableError(Type page, string loading, string error)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"netpilot-batch-c-failure-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var pending = new TaskCompletionSource<IReadOnlyList<DeviceUsageState>>(TaskCreationOptions.RunContinuationsAsynchronously);
            await using var services = ShellRenderingTests.Services(directory, configure: collection =>
                collection.AddSingleton<IUsageStore>(provider => new DeferredUsageStore(new LiteUsageStore(provider.GetRequiredService<NetPilotDatabase>()), pending)));
            await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
            var rendered = await renderer.Dispatcher.InvokeAsync(() => renderer.BeginRenderingComponent(page, ParameterView.Empty));
            Assert.Contains(loading, await renderer.Dispatcher.InvokeAsync(rendered.ToHtmlString));
            pending.SetException(new IOException("private failure text"));
            await rendered.QuiescenceTask;
            var html = await renderer.Dispatcher.InvokeAsync(rendered.ToHtmlString);
            Assert.Contains(error, html);
            Assert.DoesNotContain("private failure text", html);
            Assert.DoesNotContain("0 GB", html);
        }
        finally { Directory.Delete(directory, true); }
    }

    private sealed class DeferredUsageStore(IUsageStore inner, TaskCompletionSource<IReadOnlyList<DeviceUsageState>> pending) : IUsageStore
    {
        public Task<IReadOnlyList<DeviceUsageState>> GetAllStatesAsync(CancellationToken ct) => pending.Task.WaitAsync(ct);
        public Task<DeviceUsageState?> FindStateAsync(MacAddress mac, CancellationToken ct) => inner.FindStateAsync(mac, ct);
        public Task UpsertStateAsync(DeviceUsageState state, CancellationToken ct) => inner.UpsertStateAsync(state, ct);
        public Task AppendHistoryAsync(UsageHistoryEntry entry, CancellationToken ct) => inner.AppendHistoryAsync(entry, ct);
        public Task<IReadOnlyList<UsageHistoryEntry>> GetAllHistoryAsync(CancellationToken ct) => inner.GetAllHistoryAsync(ct);
        public Task AppendDailyHistoryAsync(UsageDailyHistoryEntry entry, CancellationToken ct) => inner.AppendDailyHistoryAsync(entry, ct);
        public Task<IReadOnlyList<UsageDailyHistoryEntry>> GetAllDailyHistoryAsync(CancellationToken ct) => inner.GetAllDailyHistoryAsync(ct);
    }

    private static async Task<string> RenderAsync(ServiceProvider services, Type page)
    {
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var rendered = await renderer.RenderComponentAsync(page, ParameterView.Empty);
            return rendered.ToHtmlString();
        });
    }
}
