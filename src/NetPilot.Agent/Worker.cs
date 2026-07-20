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
/// retries next tick instead.
/// </summary>
public class Worker(
    ILogger<Worker> logger,
    IConfiguration configuration,
    IPolicyStore policyStore,
    IRouterConnectionStore connectionStore,
    RouterPasswordProtector passwordProtector,
    IRouterProvider routerProvider,
    PolicyReconciliationService reconciliationService,
    UsageTrackingService usageTrackingService) : BackgroundService
{
    private bool _connected;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await policyStore.EnsureSeedCategoriesAsync(stoppingToken);
        await SeedConnectionFromEnvironmentAsync(stoppingToken);

        var pollInterval = TimeSpan.FromSeconds(configuration.GetValue("NetPilot:PollIntervalSeconds", 180));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!_connected && !await TryConnectAsync(stoppingToken))
                {
                    logger.LogWarning(
                        "No router configured yet — set it from the dashboard, or ROUTER_HOST/ROUTER_PASSWORD env vars on first run. Retrying in {Interval}.",
                        pollInterval);
                }
                else
                {
                    var snapshots = await reconciliationService.ReconcileAsync(routerProvider, stoppingToken);
                    await usageTrackingService.TrackAsync(snapshots, stoppingToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Reconciliation tick failed — will reconnect and retry next tick.");
                _connected = false;
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

    private async Task<bool> TryConnectAsync(CancellationToken ct)
    {
        var connection = await connectionStore.GetAsync(ct);
        if (connection is null)
            return false;

        var password = passwordProtector.Decrypt(connection.EncryptedPassword);
        var settings = new RouterConnectionSettings(connection.Host, connection.UseHttps, connection.Username, password);

        await routerProvider.ConnectAsync(settings, ct);
        _connected = true;
        logger.LogInformation("Connected to router at {Host}", connection.Host);
        return true;
    }
}
