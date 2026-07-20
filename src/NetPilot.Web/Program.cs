using NetPilot.Core.Enforcement;
using NetPilot.Core.Usage;
using NetPilot.Data;
using NetPilot.Providers.TpLink;
using NetPilot.Web.Components;

var builder = WebApplication.CreateBuilder(args);

var dataDir = builder.Configuration["NetPilot:DataDirectory"] ?? "../../data";
Directory.CreateDirectory(dataDir);
var dbPath = Path.Combine(dataDir, "netpilot.db");
var keyRingPath = Path.Combine(dataDir, "keys");

builder.Services.AddNetPilotData(dbPath, keyRingPath);
builder.Services.AddTpLinkProvider();
builder.Services.AddSingleton<PolicyReconciliationService>();
builder.Services.AddSingleton<UsageTrackingService>();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
