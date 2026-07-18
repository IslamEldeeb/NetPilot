using NetPilot.Agent;
using NetPilot.Core.Enforcement;
using NetPilot.Data;
using NetPilot.Providers.TpLink;

var builder = Host.CreateApplicationBuilder(args);

var dataDir = builder.Configuration["NetPilot:DataDirectory"] ?? "./data";
Directory.CreateDirectory(dataDir);
var dbPath = Path.Combine(dataDir, "netpilot.db");
var keyRingPath = Path.Combine(dataDir, "keys");

builder.Services.AddNetPilotData(dbPath, keyRingPath);
builder.Services.AddTpLinkProvider();
builder.Services.AddSingleton<PolicyReconciliationService>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
