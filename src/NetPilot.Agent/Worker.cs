using NetPilot.Abstractions;
using NetPilot.Core.Enforcement;
using NetPilot.Core.Policy;
using NetPilot.Core.RouterConnection;
using NetPilot.Core.Usage;
using NetPilot.Data;

namespace NetPilot.Agent;

/// <summary>
/// The reconciliation loop: one read, per-device fingerprint compare, write only what's
/// wrong — every {PollIntervalSeconds} (default 30s). Never crashes the whole worker on a
/// single bad tick (router offline, bad password, transient network error); logs and
/// retries next tick instead. Connecting and every router call go through
/// RouterSessionManager so this tick can never collide with a Web dashboard request (or,
/// later, the Home Assistant API) for the router's single login slot.
/// </summary>
public class Worker(
    ILogger<Worker> logger,
    IConfiguration configuration,
    IPolicyStore policyStore,
    IRouterConnectionStore connectionStore,
    RouterPasswordProtector passwordProtector,
    IRouterProvider routerProvider,
    RouterSessionManager sessionManager,
    PolicyReconciliationService reconciliationService,
    UsageTrackingService usageTrackingService) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await policyStore.EnsureSeedCategoriesAsync(stoppingToken);
        await SeedConnectionFromEnvironmentAsync(stoppingToken);

        var pollInterval = TimeSpan.FromSeconds(configuration.GetValue("NetPilot:PollIntervalSeconds", 180));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var snapshots = await sessionManager.ExecuteAsync(
                    p => reconciliationService.ReconcileAsync(p, stoppingToken), stoppingToken);
                await usageTrackingService.TrackAsync(snapshots, stoppingToken);
            }
            catch (RouterNotConfiguredException)
            {
                logger.LogWarning(
                    "No router configured yet — set it from the dashboard, or ROUTER_HOST/ROUTER_PASSWORD env vars on first run. Retrying in {Interval}.",
                    pollInterval);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Reconciliation tick failed — will reconnect and retry next tick.");
            }

            await Task.Delay(pollInterval, stoppingToken);
        }
    }

    /// <summary>Convenience for `docker compose up` first run — ROUTER_HOST/ROUTER_PASSWORD seed the record if it's empty. No-op after that; the dashboard is the source of truth.</summary>
    private async Task SeedConnectionFromEnvironmentAsync(CancellationToken ct)
    {
        var envHost = Environment.GetEnvironmentVariable("ROUTER_HOST");
        var envPassword = Environment.GetEnvironmentVariable("ROUTER_PASSWORD");
        if (string.IsNullOrWhiteSpace(envHost) || string.IsNullOrWhiteSpace(envPassword))
            return;

        await connectionStore.SeedFromEnvironmentIfEmptyAsync(
            routerProvider.ProviderId, envHost, passwordProtector.Encrypt(envPassword), ct);
    }
}
