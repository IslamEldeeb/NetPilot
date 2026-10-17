using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Forms;
using NetPilot.Core.Health;
using NetPilot.Core.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using MudBlazor.Services;
using NetPilot.Core.Enforcement;
using NetPilot.Core.Devices;
using NetPilot.Web.Components.DesignSystem;
using NetPilot.Core.Rules;
using NetPilot.Core.Quota;
using NetPilot.Core.Enforcement.Claims;
using NetPilot.Core.Usage;
using NetPilot.Notifications.WebPush;
using NetPilot.Providers.TpLink;
using NetPilot.Web;
using NetPilot.Web.Components.Layout;
using NetPilot.Web.Components.Pages;

namespace NetPilot.Data.Tests.Web;

public sealed class ShellRenderingTests
{
    [Theory]
    [InlineData(typeof(Home), "Home network", "/legacy")]
    [InlineData(typeof(Devices), "Devices", "/legacy#devices")]
    [InlineData(typeof(Usage), "Usage", "/legacy#usage")]
    [InlineData(typeof(Automations), "Global default", "/legacy#policies")]
    [InlineData(typeof(Activity), "Activity", "/legacy#activity")]
    [InlineData(typeof(Settings), "Open Router", "/legacy#notifications")]
    [InlineData(typeof(Safety), "Evidence checklist", "/settings")]
    [InlineData(typeof(Setup), "Step 1 of 6", "/settings")]
    public async Task PlaceholderInMudShell_RendersContentAndClassicEscape(Type page, string content, string classicPath)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"netpilot-mud-render-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            await using var provider = Services(directory);
            await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
            var html = await renderer.Dispatcher.InvokeAsync(async () =>
            {
                RenderFragment body = builder => { builder.OpenComponent(0, page); builder.CloseComponent(); };
                var shell = await renderer.RenderComponentAsync<AppShell>(ParameterView.FromDictionary(
                    new Dictionary<string, object?> { [nameof(AppShell.Body)] = body }));
                return shell.ToHtmlString();
            });
            Assert.Contains(content, html);
            Assert.Contains(classicPath, html);
            Assert.Contains("Home network / Cairo", html);
            Assert.Contains("Rules: Forced dry-run", html);
            Assert.Contains("aria-current=\"page\"", html);
            Assert.Contains("id=\"blazor-error-ui\"", html);
            Assert.DoesNotContain("Could not load", html);
            Assert.DoesNotContain("Sign out", html);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData("AA-BB-CC-DD-EE-01", true, "Living room TV")]
    [InlineData("AA-BB-CC-DD-EE-02", false, "Device not found")]
    [InlineData("not-a-mac", false, "Invalid device address")]
    public async Task DeviceDetail_RendersInsideShell(string mac, bool seed, string expected)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"netpilot-detail-render-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            await using var provider = Services(directory);
            if (seed) await provider.GetRequiredService<IDeviceStore>().UpsertAsync(new Device
            {
                Mac = new(mac), FriendlyName = "Living room TV", Hostname = "tv", CategoryKey = "television",
                FirstSeenAtUtc = DateTimeOffset.UtcNow.AddDays(-2), LastSeenAtUtc = DateTimeOffset.UtcNow
            }, CancellationToken.None);
            await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
            var html = await renderer.Dispatcher.InvokeAsync(async () =>
            {
                RenderFragment body = builder =>
                {
                    builder.OpenComponent<DevicePage>(0);
                    builder.AddAttribute(1, nameof(DevicePage.Mac), mac);
                    builder.CloseComponent();
                };
                var shell = await renderer.RenderComponentAsync<AppShell>(ParameterView.FromDictionary(
                    new Dictionary<string, object?> { [nameof(AppShell.Body)] = body }));
                return shell.ToHtmlString();
            });
            Assert.Contains(expected, html);
            Assert.Contains("All devices", html);
            Assert.Contains("Home network / Cairo", html);
            Assert.Contains("Quotas: Not set", html);
            Assert.Contains("Agent: Unknown", html);
            Assert.DoesNotContain("Could not load", html);
            if (seed)
            {
                Assert.Contains("Who is holding this limit?", html);
                Assert.Contains("Last 30 days", html);
                Assert.Contains("LAN-only device", html);
                Assert.Contains("No automatic restriction claims", html);
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task SavedDeviceList_AndEnabledLogout_RenderRealControls()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"netpilot-list-render-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            await using var provider = Services(directory, loginEnabled: true);
            await provider.GetRequiredService<IDeviceStore>().UpsertAsync(new Device
            {
                Mac = new("AA-BB-CC-DD-EE-01"), FriendlyName = "My phone", CategoryKey = "mobile",
                Connection = new(NetPilot.Abstractions.ConnectionMedium.Wifi5Ghz, true),
                FirstSeenAtUtc = DateTimeOffset.UtcNow.AddDays(-2)
            }, CancellationToken.None);
            await provider.GetRequiredService<IAgentHeartbeatStore>().SaveAsync(new AgentHeartbeat
            {
                LastTickStartedUtc = DateTimeOffset.UtcNow.AddSeconds(-10),
                LastTickCompletedUtc = DateTimeOffset.UtcNow.AddSeconds(-5), TickIntervalSeconds = 180
            }, CancellationToken.None);
            await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
            var html = await renderer.Dispatcher.InvokeAsync(async () =>
            {
                RenderFragment body = builder => { builder.OpenComponent<Devices>(0); builder.CloseComponent(); };
                var shell = await renderer.RenderComponentAsync<AppShell>(ParameterView.FromDictionary(
                    new Dictionary<string, object?> { [nameof(AppShell.Body)] = body }));
                return shell.ToHtmlString();
            });
            Assert.Contains("1 online of 1", html);
            Assert.Contains("/devices/AA-BB-CC-DD-EE-01", html);
            Assert.Contains("devices-table", html);
            Assert.Contains("devices-cards", html);
            Assert.Contains("Select My phone", html);
            Assert.Contains("Router: Not observed", html);
            Assert.Contains("Agent: Healthy", html);
            Assert.Contains("method=\"post\" action=\"/logout\"", html);
            Assert.Contains("name=\"__RequestVerificationToken\"", html);
            Assert.Contains("test-antiforgery-token", html);
            Assert.Contains("aria-label=\"Sign out\"", html);
            Assert.DoesNotContain("Could not load", html);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData("aa:bb:cc:dd:ee:01")]
    [InlineData("aa-bb-cc-dd-ee-01")]
    public async Task DeviceDetail_NoncanonicalMac_RedirectsToCanonicalRoute(string mac)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"netpilot-route-render-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            await using var provider = Services(directory);
            await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
            await renderer.Dispatcher.InvokeAsync(() => renderer.RenderComponentAsync<DevicePage>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { [nameof(DevicePage.Mac)] = mac })));
            Assert.Equal("http://localhost/devices/AA-BB-CC-DD-EE-01", provider.GetRequiredService<NavigationManager>().Uri);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task DeviceDetail_WithOverlappingRestrictions_RendersOwnersAndSafeReleaseControls()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"netpilot-claims-render-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            await using var provider = Services(directory);
            var mac = new MacAddress("AA-BB-CC-DD-EE-01");
            await provider.GetRequiredService<IDeviceStore>().UpsertAsync(new Device
            {
                Mac = mac, Hostname = "tv", CategoryKey = "television", FirstSeenAtUtc = DateTimeOffset.UtcNow.AddDays(-2)
            }, CancellationToken.None);
            var day = provider.GetRequiredService<UsagePeriodClock>().TodayKey();
            await provider.GetRequiredService<IQuotaStore>().UpsertAsync(new QuotaPolicy
            {
                Id = "cat:television", Scope = QuotaScope.Category, CategoryKey = "television",
                DailyBytes = 5_000_000_000, MonthlyBytes = 100_000_000_000,
                Action = QuotaAction.Throttle, ThrottleKbps = 512
            }, CancellationToken.None);
            await provider.GetRequiredService<IQuotaStateStore>().UpsertAsync(new QuotaPeriodState
            {
                Mac = mac, PeriodKind = QuotaPeriodKind.Day, PeriodKey = day, ActionKind = QuotaActionKind.Throttle,
                ActionAppliedAtUtc = DateTimeOffset.UtcNow, ExtraAllowanceBytes = 1_000_000_000,
                CapBytes = 5_000_000_000, UsedBytes = 6_000_000_000
            }, CancellationToken.None);
            var claim = new ActionClaim(mac, ClaimKind.Throttle, $"quota:day:{day}", 512, DateTimeOffset.UtcNow);
            await provider.GetRequiredService<IActionClaimStore>().UpsertAsync(claim, CancellationToken.None);
            var firing = new RuleFiring
            {
                RuleId = Guid.NewGuid(), RuleName = "Bedtime", Subject = mac, EpisodeKey = "window",
                TargetMacs = [mac], Actions = [new Block()], Outcome = RuleFiringOutcome.Applied,
                FiredAtUtc = DateTimeOffset.UtcNow, AppliedClaimIds = []
            };
            var ruleClaim = new ActionClaim(mac, ClaimKind.Block, $"rule:{firing.RuleId}", null, DateTimeOffset.UtcNow);
            await provider.GetRequiredService<IActionClaimStore>().UpsertAsync(ruleClaim, CancellationToken.None);
            firing = firing with { AppliedClaimIds = [ruleClaim.Id] };
            await provider.GetRequiredService<IRuleFiringStore>().UpdateAsync(firing.Id, _ => firing, CancellationToken.None);
            await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
            var html = await renderer.Dispatcher.InvokeAsync(async () =>
            {
                var page = await renderer.RenderComponentAsync<DevicePage>(ParameterView.FromDictionary(
                    new Dictionary<string, object?> { [nameof(DevicePage.Mac)] = mac.ToString() }));
                return page.ToHtmlString();
            });
            Assert.Contains("Release this quota claim", html);
            Assert.Contains("Release this rule firing", html);
            Assert.Contains("Bedtime", html);
            Assert.Contains("A block is active", html);
            Assert.Contains("512 Kbps", html);
            Assert.Contains("Inherits Category Television", html);
            Assert.Contains("dry-run", html);
            Assert.Contains("effective cap 6 GB", html);
            Assert.DoesNotContain("Could not load", html);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Theory]
    [InlineData("router", "Router connection")]
    [InlineData("wireless", "Main WiFi")]
    [InlineData("notifications", "Shared notification preferences")]
    [InlineData("health", "LAN exclusion / T8")]
    [InlineData("traffic", "Accuracy: Not automatically verified")]
    [InlineData("app", "HTTPS and installation")]
    [InlineData("about", "Database size is not measured")]
    [InlineData("appearance", "System")]
    [InlineData("backup", "Download backup")]
    [InlineData("security", "Dashboard access")]
    public async Task SettingsSectionsRenderRealControlsWithoutSecrets(string section, string expected)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"netpilot-settings-render-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            await using var provider = Services(directory);
            await provider.GetRequiredService<NetPilot.Core.RouterConnection.IRouterConnectionStore>().SaveAsync(new("tplink", "192.168.1.1", false, "admin", "saved-secret-ciphertext"), default);
            await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
            var html = await renderer.Dispatcher.InvokeAsync(async () =>
            {
                var page = await renderer.RenderComponentAsync<Settings>(ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(Settings.Section)] = section }));
                return page.ToHtmlString();
            });
            Assert.Contains(expected, html);
            Assert.DoesNotContain("saved-secret-ciphertext", html);
            Assert.DoesNotContain("Could not load", html);
            Assert.DoesNotContain("np-", html);
            if (section == "backup")
            {
                Assert.Contains("method=\"post\" action=\"/api/backup/download\"", html);
                Assert.Contains("test-antiforgery-token", html);
                Assert.Contains("docs/backup-restore.md", html);
            }
            if (section == "router") Assert.Contains("type=\"password\"", html);
            if (section == "notifications") Assert.Contains("not prove Android display", html);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(1, "Discover devices")]
    [InlineData(2, "Categories and protection")]
    [InlineData(3, "Optional safe quota")]
    [InlineData(4, "Optional notifications")]
    [InlineData(5, "Ready to observe")]
    public async Task SetupResumesAtSavedStepWithSkipAndRetry(int step, string expected)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"netpilot-setup-render-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            await using var provider = Services(directory);
            await provider.GetRequiredService<NetPilot.Core.Settings.ISetupProgressStore>().SaveAsync(new(step), default);
            await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
            var html = await renderer.Dispatcher.InvokeAsync(async () => (await renderer.RenderComponentAsync<Setup>()).ToHtmlString());
            Assert.Contains(expected, html); Assert.Contains("Skip setup for now", html); Assert.Contains("Retry", html);
            Assert.DoesNotContain("Could not load", html);
        }
        finally { Directory.Delete(directory, true); }
    }

    internal static ServiceProvider Services(string directory, bool loginEnabled = false, Action<IServiceCollection>? configure = null)
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection().AddLogging()
            .AddNetPilotData(Path.Combine(directory, "test.db"), Path.Combine(directory, "keys"));
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(new UsagePeriodClock());
        services.AddSingleton(new RulesOptions { ForceDryRun = true });
        services.AddSingleton<NavigationManager, StaticNavigation>();
        services.AddSingleton<IJSRuntime, StaticJs>();
        services.AddMudServices();
        services.AddScoped<Confirm>();
        services.AddScoped<DeviceRouterActions>();
        services.AddScoped<NetPilot.Web.Features.UsageOverviewQuery>();
        services.AddSingleton(new NetPilot.Core.Actions.TemporaryActionOptions());
        services.AddScoped<SettingsRouterActions>();
        services.AddScoped<NetPilot.Core.Safety.SafetyGraduationService>();
        services.AddDashboardLogin(configuration);
        if (loginEnabled) services.AddSingleton(new DashboardLoginOptions("test-password"));
        services.AddSingleton<AntiforgeryStateProvider, StaticAntiforgery>();
        services.AddTpLinkProvider();
        services.AddNetPilotWebPush(configuration);
        services.AddNetPilotLocalTraffic(configuration);
        services.AddSingleton<PolicyReconciliationService>().AddSingleton<UsageTrackingService>();
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    private sealed class StaticAntiforgery : AntiforgeryStateProvider
    {
        public override AntiforgeryRequestToken GetAntiforgeryToken() => new("test-antiforgery-token", "__RequestVerificationToken");
    }

    // HtmlRenderer has no browser. Mud cleanup calls are no-ops at this boundary.
    private sealed class StaticJs : IJSRuntime
    {
        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) => ValueTask.FromResult(default(T)!);
        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken cancellationToken, object?[]? args) => ValueTask.FromResult(default(T)!);
    }

    private sealed class StaticNavigation : NavigationManager
    {
        public StaticNavigation() => Initialize("http://localhost/", "http://localhost/");
        protected override void NavigateToCore(string uri, bool forceLoad) => Uri = ToAbsoluteUri(uri).AbsoluteUri;
        protected override void NavigateToCore(string uri, NavigationOptions options) => Uri = ToAbsoluteUri(uri).AbsoluteUri;
    }
}
