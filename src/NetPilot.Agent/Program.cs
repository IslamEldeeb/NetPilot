using NetPilot.Agent;
using NetPilot.Core.Enforcement;
using NetPilot.Core.Enforcement.Claims;
using NetPilot.Core.Notifications;
using NetPilot.Core.Quota;
using NetPilot.Core.Rules;
using NetPilot.Core.Usage;
using NetPilot.Data;
using NetPilot.Notifications.WebPush;
using NetPilot.Providers.TpLink;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton(new UsagePeriodClock(
    builder.Configuration["NetPilot:TimeZone"] ?? UsagePeriodClock.DefaultTimeZoneId));

var dataDir = builder.Configuration["NetPilot:DataDirectory"] ?? "../../data";
Directory.CreateDirectory(dataDir);
var dbPath = Path.Combine(dataDir, "netpilot.db");
var keyRingPath = Path.Combine(dataDir, "keys");

builder.Services.AddNetPilotData(dbPath, keyRingPath);
builder.Services.AddTpLinkProvider();
builder.Services.AddNetPilotWebPush(builder.Configuration);
builder.Services.AddSingleton<PolicyReconciliationService>();
builder.Services.AddNetPilotLocalTraffic(builder.Configuration);
builder.Services.AddSingleton<UsageTrackingService>();
builder.Services.AddSingleton<IQuotaEventSink, NullQuotaEventSink>();
builder.Services.AddSingleton<IQuotaEventSink, ActivityLogQuotaEventSink>();
builder.Services.AddSingleton<IQuotaEventSink, NotificationQuotaEventSink>();
builder.Services.AddSingleton<ClaimEnforcementService>();
builder.Services.AddSingleton<QuotaEnforcementService>();
var rulesOptions = new RulesOptions();
builder.Configuration.GetSection("NetPilot:Rules").Bind(rulesOptions);
rulesOptions.Validate();
builder.Services.AddSingleton(rulesOptions);
builder.Services.AddSingleton<RuleEvaluationService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
