using MudBlazor.Services;
using NetPilot.Web.Components.DesignSystem;
using NetPilot.Core.Enforcement;
using NetPilot.Core.Rules;
using NetPilot.Core.Usage;
using NetPilot.Data;
using NetPilot.Notifications.WebPush;
using NetPilot.Providers.TpLink;
using NetPilot.Web.Components;
using NetPilot.Web;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(new UsagePeriodClock(
    builder.Configuration["NetPilot:TimeZone"] ?? UsagePeriodClock.DefaultTimeZoneId));

var dataDir = builder.Configuration["NetPilot:DataDirectory"] ?? "../../data";
Directory.CreateDirectory(dataDir);
var dbPath = Path.Combine(dataDir, "netpilot.db");
var keyRingPath = Path.Combine(dataDir, "keys");

var rulesOptions = new RulesOptions();
builder.Configuration.GetSection("NetPilot:Rules").Bind(rulesOptions);
rulesOptions.Validate();
builder.Services.AddSingleton(rulesOptions);

builder.Services.AddNetPilotData(dbPath, keyRingPath);
builder.Services.AddTpLinkProvider();
builder.Services.AddNetPilotWebPush(builder.Configuration);
builder.Services.AddNotificationActionEndpoints();
builder.Services.AddDashboardLogin(builder.Configuration);
builder.Services.AddBackupEndpoints(builder.Configuration);
builder.Services.AddSingleton<PolicyReconciliationService>();
builder.Services.AddNetPilotLocalTraffic(builder.Configuration);
builder.Services.AddSingleton<UsageTrackingService>();
builder.Services.AddMudServices(options => options.ResizeOptions.BreakpointDefinitions = NetPilotTheme.ViewportBreakpoints);
builder.Services.AddScoped<Confirm>();
builder.Services.AddScoped<DeviceRouterActions>();
builder.Services.AddScoped<SettingsRouterActions>();
builder.Services.AddScoped<NetPilot.Core.Safety.SafetyGraduationService>();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();
app.UseDashboardLogin();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
// HTTP localhost is a secure context in Chrome; deployment HTTPS is configured separately.
app.Use(async (context, next) =>
{
    if (context.Request.Path == "/sw.js" && HttpMethods.IsGet(context.Request.Method))
    {
        context.Response.ContentType = "text/javascript; charset=utf-8";
        context.Response.Headers.CacheControl = "no-cache, no-store, max-age=0";
        await context.Response.SendFileAsync(Path.Combine(app.Environment.WebRootPath, "sw.js"), context.RequestAborted);
        return;
    }
    await next();
});
app.UseRateLimiter();

app.UseAntiforgery();

app.MapDashboardLogin();
app.MapNotificationActionEndpoints();
app.MapBackupEndpoints();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
