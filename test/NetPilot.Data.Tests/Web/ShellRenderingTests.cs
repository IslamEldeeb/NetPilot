using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using MudBlazor.Services;
using NetPilot.Core.Enforcement;
using NetPilot.Core.Rules;
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
    [InlineData(typeof(Home), "Welcome to the new NetPilot", "/legacy")]
    [InlineData(typeof(Devices), "Devices", "/legacy#devices")]
    [InlineData(typeof(Usage), "Usage", "/legacy#usage")]
    [InlineData(typeof(Automations), "Global default", "/legacy#policies")]
    [InlineData(typeof(Activity), "Activity", "/legacy#activity")]
    [InlineData(typeof(Settings), "This browser", "/legacy#notifications")]
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

    private static ServiceProvider Services(string directory)
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
        services.AddDashboardLogin(configuration);
        services.AddTpLinkProvider();
        services.AddNetPilotWebPush(configuration);
        services.AddNetPilotLocalTraffic(configuration);
        services.AddSingleton<PolicyReconciliationService>().AddSingleton<UsageTrackingService>();
        return services.BuildServiceProvider();
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
    }
}
